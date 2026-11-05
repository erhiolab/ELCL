using System.IO;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Core.Storage;

namespace ErhioLab.Craft.Launcher.Accounts;

internal sealed record OfflineAccount(string UserName, Guid Uuid);

internal sealed record AccountFile(List<OfflineAccount> OfflineAccounts, Guid? SelectedUuid);

/// <summary>账户独立保存在 accounts.json，不参与启动器设置导入和重置.</summary>
internal sealed class LauncherAccountStore
{
    private readonly JsonFileStore<AccountFile> _store;
    private AccountFile _data = new([], null);
    private readonly bool _canWrite = true;

    internal IReadOnlyList<OfflineAccount> Accounts => _data.OfflineAccounts;
    internal Guid? SelectedUuid => _data.SelectedUuid;
    internal OfflineAccount? SelectedAccount => _data.OfflineAccounts.FirstOrDefault(account => account.Uuid == SelectedUuid);
    internal event EventHandler? Changed;

    internal LauncherAccountStore(string path)
    {
        _store = new JsonFileStore<AccountFile>(path, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            AccountFile loaded = _store.Load(new AccountFile([], null));
            if (loaded.OfflineAccounts is null) throw new JsonException("账户列表缺失. ");
            List<OfflineAccount> valid = loaded.OfflineAccounts
                .Where(account => IsValidName(account.UserName) && account.Uuid == OfflinePlayerId.FromName(account.UserName))
                .DistinctBy(account => account.Uuid)
                .ToList();
            _data = new AccountFile(valid, valid.Any(account => account.Uuid == loaded.SelectedUuid) ? loaded.SelectedUuid : null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _canWrite = false;
            App.Logger.Error("Accounts", "读取账户文件失败, 本次运行使用空账户列表; 原文件已保留. ", exception);
        }
    }

    internal static bool IsValidName(string? name) => name is { Length: >= 3 and <= 16 } && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    internal bool TryAdd(string name, out string error)
    {
        name = name.Trim();
        if (!IsValidName(name))
        {
            error = "用户名需要 3–16 位, 只能包含英文字母/数字和下划线. ";
            return false;
        }

        if (_data.OfflineAccounts.Any(account => string.Equals(account.UserName, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "这个用户名已经在账户列表中. ";
            return false;
        }

        List<OfflineAccount> accounts = [.. _data.OfflineAccounts, new OfflineAccount(name, OfflinePlayerId.FromName(name))];
        Save(new AccountFile(accounts, _data.SelectedUuid));
        error = string.Empty;
        return true;
    }

    internal void Select(Guid uuid)
    {
        if (_data.SelectedUuid == uuid) return;
        if (!_data.OfflineAccounts.Any(account => account.Uuid == uuid)) throw new ArgumentException("账户不存在. ", nameof(uuid));
        Save(_data with { SelectedUuid = uuid });
    }

    internal void Remove(Guid uuid)
    {
        List<OfflineAccount> accounts = _data.OfflineAccounts.Where(account => account.Uuid != uuid).ToList();
        if (accounts.Count == _data.OfflineAccounts.Count) return;
        Save(new AccountFile(accounts, _data.SelectedUuid == uuid ? null : _data.SelectedUuid));
    }

    private void Save(AccountFile next)
    {
        if (!_canWrite) throw new IOException("账户文件无法读取. 请先备份并修复 ELCL/accounts.json. 原文件未被覆盖. ");
        _store.Save(next);
        _data = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
