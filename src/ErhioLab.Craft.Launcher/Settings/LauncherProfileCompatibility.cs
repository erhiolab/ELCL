using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>维护 ELCL 创建的官方格式安装档案, 不导入或导出账户.</summary>
internal static class LauncherProfileCompatibility
{
    internal static async Task InitializeFolderAsync(string gameDirectory)
    {
        await EnsureFolderAsync(gameDirectory);
        try
        {
            var versions = await Task.Run(() => LocalMinecraftVersionScanner.Scan(gameDirectory,
                (path, error) => App.Logger.Error("Profiles", $"登记安装档案时跳过无法读取的版本: {path}.", error)));
            await SynchronizeVersionsAsync(gameDirectory, versions);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Logger.Error("Profiles", $"扫描已有版本以补全安装档案失败: {gameDirectory}.", error);
        }
    }

    internal static async Task SynchronizeVersionsAsync(string gameDirectory, IReadOnlyList<LocalMinecraftVersion> versions)
    {
        await EnsureFolderAsync(gameDirectory);
        if (versions.Count == 0) return;
        var profiles = versions.ToDictionary(version => ProfileId(version.FolderPath), version =>
            new MinecraftLauncherProfile(version.Id, Path.GetFileName(version.FolderPath), version.FolderPath));
        await UpdateAsync(gameDirectory, $"目录={gameDirectory}, 版本数={profiles.Count}",
            store => store.UpsertProfilesAsync(profiles), CancellationToken.None, notifyFailure: false);
    }

    internal static async Task EnsureFolderAsync(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, MinecraftLauncherProfilesStore.LegacyFileName);
        try
        {
            if (await new MinecraftLauncherProfilesStore(path).EnsureCreatedAsync())
                App.Logger.Information("Profiles", $"已创建启动器兼容档案文件: {path}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Logger.Error("Profiles", $"无法创建兼容档案文件, 保留现有数据: {path}.", error);
        }
    }

    internal static Task RegisterVersionAsync(string gameDirectory, string versionDirectory, string versionId,
        string displayName, bool markUsed = false, CancellationToken cancellationToken = default) =>
        UpdateAsync(gameDirectory, versionDirectory, store => store.UpsertProfileAsync(ProfileId(versionDirectory),
            new MinecraftLauncherProfile(displayName, versionId, versionDirectory), markUsed, cancellationToken), cancellationToken);

    internal static Task RemoveVersionAsync(string gameDirectory, string versionDirectory) =>
        UpdateAsync(gameDirectory, versionDirectory, store => store.RemoveProfileAsync(ProfileId(versionDirectory)), CancellationToken.None);

    private static async Task UpdateAsync(string gameDirectory, string description,
        Func<MinecraftLauncherProfilesStore, Task> update, CancellationToken cancellationToken, bool notifyFailure = true)
    {
        var legacy = Path.Combine(gameDirectory, MinecraftLauncherProfilesStore.LegacyFileName);
        var microsoftStore = Path.Combine(gameDirectory, MinecraftLauncherProfilesStore.MicrosoftStoreFileName);
        string[] paths = File.Exists(microsoftStore) ? [legacy, microsoftStore] : [legacy];
        var failed = false;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                App.Logger.Information("Profiles", $"开始同步 ELCL 安装档案: {description}, 文件={path}.");
                await update(new MinecraftLauncherProfilesStore(path));
                App.Logger.Information("Profiles", $"已同步 ELCL 安装档案: {description}, 文件={path}.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                failed = true;
                App.Logger.Error("Profiles", $"兼容档案同步失败, 未覆盖损坏或被占用的文件: {path}.", error);
            }
        }

        if (failed && notifyFailure) LauncherToasts.Information(LocalizationManager.Get("profiles.syncFailed"));
    }

    private static string ProfileId(string versionDirectory)
    {
        var path = Path.GetFullPath(versionDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Windows 路径大小写不产生第二份档案; 固定命名空间避免与用户创建的 UUID 混用.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("org.erhiolab.elcl.profile|" + path.ToUpperInvariant()));
        return new Guid(hash.AsSpan(0, 16)).ToString("N");
    }
}
