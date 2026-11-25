using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

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
    [property: JsonRequired] List<OfflineAccount> OfflineAccounts,
    Guid? SelectedUuid,
    List<MicrosoftAccount>? MicrosoftAccounts = null);

internal readonly record struct AccountImportResult(int Added, int UnavailableMicrosoft);

/// <summary>账户独立保存在 accounts.json, 不参与启动器设置导入和重置.</summary>
internal sealed class LauncherAccountStore
{
    private readonly LauncherDataFile<AccountFile> _store;
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
        _store = new LauncherDataFile<AccountFile>(path, LauncherDataKind.Accounts);
        try
        {
            AccountFile loaded = _store.Load(new AccountFile([], null));
            if (loaded.OfflineAccounts is null) throw new JsonException("账户列表缺失.");
            List<OfflineAccount> valid = loaded.OfflineAccounts
                .Where(IsValidOfflineAccount)
                .DistinctBy(account => account.Uuid)
                .ToList();
            List<MicrosoftAccount> microsoft = (loaded.MicrosoftAccounts ?? [])
                .Where(account => account is not null && account.Uuid != Guid.Empty && !string.IsNullOrWhiteSpace(account.UserName) && !string.IsNullOrWhiteSpace(account.ProtectedRefreshToken))
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

    private static bool IsValidOfflineAccount(OfflineAccount? account) => account is not null
        && IsValidName(account.UserName) && account.Uuid == OfflinePlayerId.FromName(account.UserName);

    internal void ExportBackup(string path)
    {
        EnsureWritable();
        App.ValidateBackupDestination(path);
        // 直接保存 Windows 保护后的凭据, 备份不写明文令牌.
        new LauncherDataFile<AccountFile>(path, LauncherDataKind.AccountsBackup).Save(_data);
    }

    internal AccountImportResult ImportBackup(string path)
    {
        EnsureWritable();
        var incoming = new LauncherDataFile<AccountFile>(path, LauncherDataKind.AccountsBackup).Read();
        if (incoming.OfflineAccounts is null || incoming.OfflineAccounts.Any(account => !IsValidOfflineAccount(account)))
            throw new JsonException(LocalizationManager.Get("settings.accountsInvalidList"));

        List<OfflineAccount> offline = [.. _data.OfflineAccounts];
        List<MicrosoftAccount> microsoft = [.. MicrosoftAccounts];
        var offlineNames = offline.Select(account => account.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var microsoftIds = microsoft.Select(account => account.Uuid).ToHashSet();
        int unavailable = 0;
        foreach (var account in incoming.OfflineAccounts)
        {
            if (offlineNames.Add(account.UserName)) offline.Add(account);
        }
        foreach (var account in incoming.MicrosoftAccounts ?? [])
        {
            if (account is not null && microsoftIds.Contains(account.Uuid)) continue;
            if (!CanRestoreMicrosoftAccount(account))
            {
                unavailable++;
                continue;
            }
            microsoft.Add(account!);
            microsoftIds.Add(account!.Uuid);
        }

        int added = offline.Count - _data.OfflineAccounts.Count + microsoft.Count - MicrosoftAccounts.Count;
        Guid? selected = _data.SelectedUuid;
        if (selected is null && incoming.SelectedUuid is { } uuid && (offline.Any(account => account.Uuid == uuid) || microsoftIds.Contains(uuid)))
            selected = uuid;
        if (added != 0 || selected != _data.SelectedUuid) Save(new AccountFile(offline, selected, microsoft));
        return new AccountImportResult(added, unavailable);
    }

    private static bool CanRestoreMicrosoftAccount(MicrosoftAccount? account)
    {
        if (account is null || account.Uuid == Guid.Empty || string.IsNullOrWhiteSpace(account.UserName)
            || string.IsNullOrWhiteSpace(account.ProtectedRefreshToken) || string.IsNullOrWhiteSpace(account.ProtectedMinecraftAccessToken)) return false;
        try
        {
            return !string.IsNullOrWhiteSpace(Unprotect(account.ProtectedRefreshToken))
                && !string.IsNullOrWhiteSpace(Unprotect(account.ProtectedMinecraftAccessToken));
        }
        catch (Exception error) when (error is CryptographicException or FormatException) { return false; }
    }

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
        EnsureWritable();
        _store.Save(next);
        _data = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureWritable()
    {
        if (!_canWrite) throw new IOException(LocalizationManager.Get("account.fileUnreadable"));
    }
}
