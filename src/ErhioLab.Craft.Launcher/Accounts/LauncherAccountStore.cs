using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Accounts;

internal sealed record OfflineAccount(string UserName, Guid Uuid);

internal sealed record MicrosoftAccount(
    string UserName,
    Guid Uuid,
    string ProtectedRefreshToken,
    string ProtectedMinecraftAccessToken,
    DateTimeOffset AccessTokenExpiresAt);

internal sealed record AccountEntry(string UserName, Guid Uuid, bool IsMicrosoft);

internal sealed record AccountFile(
    List<OfflineAccount> OfflineAccounts,
    Guid? SelectedUuid,
    List<MicrosoftAccount>? MicrosoftAccounts = null);

/// <summary>账户独立保存在 accounts.json, 不参与启动器设置导入和重置.</summary>
internal sealed class LauncherAccountStore
{
    private readonly JsonFileStore<AccountFile> _store;
    private AccountFile _data = new([], null);
    private readonly bool _canWrite = true;

    internal IReadOnlyList<OfflineAccount> Accounts => _data.OfflineAccounts;
    internal IReadOnlyList<MicrosoftAccount> MicrosoftAccounts => _data.MicrosoftAccounts ?? [];

    internal IReadOnlyList<AccountEntry> AllAccounts =>
    [
        .. MicrosoftAccounts.Select(account => new AccountEntry(account.UserName, account.Uuid, true)),
        .. Accounts.Select(account => new AccountEntry(account.UserName, account.Uuid, false))
    ];

    internal Guid? SelectedUuid => _data.SelectedUuid;
    internal AccountEntry? SelectedAccount => AllAccounts.FirstOrDefault(account => account.Uuid == SelectedUuid);
    internal event EventHandler? Changed;

    internal LauncherAccountStore(string path)
    {
        _store = new JsonFileStore<AccountFile>(path, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            AccountFile loaded = _store.Load(new AccountFile([], null));
            if (loaded.OfflineAccounts is null) throw new JsonException("账户列表缺失.");
            List<OfflineAccount> valid = loaded.OfflineAccounts
                .Where(account => IsValidName(account.UserName) && account.Uuid == OfflinePlayerId.FromName(account.UserName))
                .DistinctBy(account => account.Uuid)
                .ToList();
            List<MicrosoftAccount> microsoft = (loaded.MicrosoftAccounts ?? [])
                .Where(account => account.Uuid != Guid.Empty && !string.IsNullOrWhiteSpace(account.UserName) && !string.IsNullOrWhiteSpace(account.ProtectedRefreshToken))
                .DistinctBy(account => account.Uuid)
                .ToList();
            bool selectedExists = valid.Any(account => account.Uuid == loaded.SelectedUuid) || microsoft.Any(account => account.Uuid == loaded.SelectedUuid);
            _data = new AccountFile(valid, selectedExists ? loaded.SelectedUuid : null, microsoft);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _canWrite = false;
            App.Logger.Error("Accounts", "读取账户文件失败, 本次运行使用空账户列表; 原文件已保留.", exception);
        }
    }

    internal static bool IsValidName(string? name) => name is { Length: >= 3 and <= 16 } && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    internal bool TryAdd(string name, out string error)
    {
        name = name.Trim();
        if (!IsValidName(name))
        {
            error = LocalizationManager.Get("account.invalidName");
            return false;
        }

        if (_data.OfflineAccounts.Any(account => string.Equals(account.UserName, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = LocalizationManager.Get("account.nameExists");
            return false;
        }

        List<OfflineAccount> accounts = [.. _data.OfflineAccounts, new OfflineAccount(name, OfflinePlayerId.FromName(name))];
        Save(_data with { OfflineAccounts = accounts });
        error = string.Empty;
        return true;
    }

    internal void Select(Guid uuid)
    {
        if (_data.SelectedUuid == uuid) return;
        if (AllAccounts.All(account => account.Uuid != uuid)) throw new ArgumentException(LocalizationManager.Get("account.notFound"), nameof(uuid));
        Save(_data with { SelectedUuid = uuid });
    }

    internal void Remove(Guid uuid)
    {
        List<OfflineAccount> accounts = _data.OfflineAccounts.Where(account => account.Uuid != uuid).ToList();
        List<MicrosoftAccount> microsoft = MicrosoftAccounts.Where(account => account.Uuid != uuid).ToList();
        if (accounts.Count == _data.OfflineAccounts.Count && microsoft.Count == MicrosoftAccounts.Count) return;
        Save(new AccountFile(accounts, _data.SelectedUuid == uuid ? null : _data.SelectedUuid, microsoft));
    }

    internal void AddOrUpdateMicrosoft(MicrosoftMinecraftSession session, bool selectAccount = true)
    {
        List<MicrosoftAccount> microsoft = MicrosoftAccounts.Where(account => account.Uuid != session.Uuid).ToList();
        microsoft.Add(new MicrosoftAccount(session.UserName, session.Uuid, Protect(session.MicrosoftRefreshToken), Protect(session.MinecraftAccessToken), session.MinecraftAccessTokenExpiresAt));
        Save(_data with { MicrosoftAccounts = microsoft, SelectedUuid = selectAccount ? session.Uuid : _data.SelectedUuid });
    }

    internal string GetMinecraftAccessToken(Guid uuid)
    {
        MicrosoftAccount account = MicrosoftAccounts.FirstOrDefault(item => item.Uuid == uuid)
            ?? throw new ArgumentException(LocalizationManager.Get("account.microsoftNotFound"), nameof(uuid));
        return Unprotect(account.ProtectedMinecraftAccessToken);
    }

    internal string GetMicrosoftRefreshToken(Guid uuid)
    {
        MicrosoftAccount account = MicrosoftAccounts.FirstOrDefault(item => item.Uuid == uuid) ?? throw new ArgumentException(LocalizationManager.Get("account.microsoftNotFound"), nameof(uuid));
        return Unprotect(account.ProtectedRefreshToken);
    }

    private static string Protect(string value) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)
    );

    private static string Unprotect(string value) => Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)
    );

    private void Save(AccountFile next)
    {
        if (!_canWrite) throw new IOException(LocalizationManager.Get("account.fileUnreadable"));
        _store.Save(next);
        _data = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
