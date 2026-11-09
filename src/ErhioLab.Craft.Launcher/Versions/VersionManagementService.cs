using System.IO;
using System.Windows;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Launching;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Tasks;

namespace ErhioLab.Craft.Launcher.Versions;

/// <summary>把版本目录操作/文件补全接入配置/安装档案和任务管理.</summary>
internal static class VersionManagementService
{
    internal static event Action<string, LocalMinecraftVersion?>? Changed;

    internal static async Task SaveDescriptionAsync(LocalMinecraftVersion version, string description)
    {
        App.Logger.Information("Versions", $"开始保存版本描述: {version.FolderPath}.");
        await Task.Run(() => new VersionSettingsStore(version.FolderPath).SaveDescription(description));
        App.Logger.Information("Versions", $"版本描述已保存: {version.FolderPath}.");
        Changed?.Invoke(version.FolderPath, version);
    }

    internal static bool SamePath(string? left, string? right) => left is not null && right is not null
                                                                                   && string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static void RequireIdle(LocalMinecraftVersion version)
    {
        string folder = Path.GetFullPath(version.FolderPath).TrimEnd(Path.DirectorySeparatorChar);
        if (App.Tasks.Tasks.Any(task => task.IsActive && task.TargetPath is { } path
                                                      && (SamePath(path, folder) || Path.GetFullPath(path).StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException(L("versionSettings.busy"));
    }

    internal static async Task<LocalMinecraftVersion?> RenameAsync(string gameDirectory, LocalMinecraftVersion version, string newName)
    {
        RequireIdle(version);
        try
        {
            MinecraftVanillaDownloadPlanner.ValidateVersionName(newName);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(L("versionSettings.invalidName"));
        }

        string destination = Path.Combine(Path.GetDirectoryName(version.FolderPath)!, newName);
        if (!SamePath(destination, version.FolderPath) && (Directory.Exists(destination) || File.Exists(destination)))
            throw new InvalidOperationException(L("versionSettings.nameExists"));
        LocalMinecraftVersion? renamed = null;
        var task = App.Tasks.Start(string.Format(L("versionSettings.renameTask"), Path.GetFileName(version.FolderPath)),
        [
            new LauncherTaskStage(L("versionSettings.rename"), async context =>
            {
                renamed = await Task.Run(() =>
                {
                    RequireUnreferenced(gameDirectory, version);
                    return MinecraftVersionDirectory.RenameAsync(gameDirectory, version.FolderPath, newName, context.CancellationToken);
                }, context.CancellationToken);
                UpdatePaths(version.FolderPath, renamed);
                await LauncherProfileCompatibility.RemoveVersionAsync(gameDirectory, version.FolderPath);
                if (renamed.IsValid) await LauncherProfileCompatibility.RegisterVersionAsync(gameDirectory, renamed.FolderPath, newName, newName);
                App.Logger.Information("Versions", $"版本已重命名: {version.FolderPath} -> {renamed.FolderPath}.");
                Changed?.Invoke(version.FolderPath, renamed);
            })
        ], version.FolderPath);
        await task.Completion;
        return task.Status == LauncherTaskStatus.Completed ? renamed : null;
    }

    internal static async Task<bool> DeleteAsync(string gameDirectory, LocalMinecraftVersion version)
    {
        RequireIdle(version);
        if (!await LauncherDialogs.ConfirmAsync(L("versionSelector.deleteVersionTitle"),
                string.Format(L("folder.confirmDeleteVersion"), Path.GetFileName(version.FolderPath)), LauncherDialogLevel.Danger, L("common.delete"))) return false;
        RequireIdle(version);
        var task = App.Tasks.Start(string.Format(L("versionSettings.deleteTask"), Path.GetFileName(version.FolderPath)),
        [
            new LauncherTaskStage(L("versionSelector.deleteVersion"), async context =>
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() =>
                {
                    RequireUnreferenced(gameDirectory, version);
                    MinecraftVersionDirectory.Delete(gameDirectory, version.FolderPath);
                }, context.CancellationToken);
                UpdatePaths(version.FolderPath, null);
                await LauncherProfileCompatibility.RemoveVersionAsync(gameDirectory, version.FolderPath);
                App.Logger.Information("Versions", $"版本目录已删除: {version.FolderPath}.");
                Changed?.Invoke(version.FolderPath, null);
            })
        ], version.FolderPath);
        await task.Completion;
        return task.Status == LauncherTaskStatus.Completed;
    }

    private static void UpdatePaths(string oldPath, LocalMinecraftVersion? replacement)
    {
        bool selected = SamePath(App.Settings.SelectedVersionPath, oldPath);
        var selectable = replacement is { IsValid: true } ? replacement : null;
        App.UpdateSettings(App.Settings with
        {
            SelectedVersionPath = selected ? selectable?.FolderPath : App.Settings.SelectedVersionPath,
            SelectedVersionId = selected ? selectable?.Id : App.Settings.SelectedVersionId,
            GameFolders = App.Settings.GameFolders.Select(folder => SamePath(folder.LastSelectedVersionPath, oldPath)
                ? folder with { LastSelectedVersionPath = selectable?.FolderPath }
                : folder).ToArray()
        });
    }

    private static void RequireUnreferenced(string gameDirectory, LocalMinecraftVersion version)
    {
        var references = MinecraftVersionDirectory.GetReferences(gameDirectory, version.FolderPath);
        if (references.Count > 0) throw new InvalidOperationException(string.Format(L("versionSettings.referenced"), string.Join(", ", references)));
    }

    internal static LauncherTask Repair(string gameDirectory, LocalMinecraftVersion version)
    {
        RequireIdle(version);
        if (!version.IsValid) throw new InvalidOperationException(L("versionSettings.invalidVersion"));
        var source = App.Settings.FileDownloadSource == VersionListSource.OfficialFirst ? MinecraftManifestSource.Official : MinecraftManifestSource.Mirror;
        var options = new DownloadOptions(App.Settings.DownloadMaxConnections, (long)(App.Settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024));
        MinecraftLaunchFilePlan? plan = null;
        string name = Path.GetFileName(version.FolderPath);
        return App.Tasks.Start(string.Format(L("versionSettings.repairTask"), name),
        [
            new LauncherTaskStage(L("gameLaunch.version"), async context =>
            {
                var launch = await MinecraftLaunchVersion.LoadAsync(gameDirectory, version.JsonPath,
                    (id, token) => Application.Current.Dispatcher.InvokeAsync(() => GameLaunchService.FetchParentAsync(id, source, options, context, token)).Task.Unwrap(), context.CancellationToken);
                plan = MinecraftLaunchFiles.Plan(launch, source);
                context.ConfigureStageWeights([1, Math.Max(1, plan.Files.Sum(LauncherTaskContext.EstimatedSize)), 64L * 1024 * 1024]);
            }),
            new LauncherTaskStage(L("gameLaunch.files"), context => GameLaunchService.VerifyAndDownloadAsync(plan!.Files, options, context, name)),
            new LauncherTaskStage(L("gameLaunch.assets"), async context =>
            {
                string index = await File.ReadAllTextAsync(plan!.AssetIndex.DestinationPath, context.CancellationToken);
                var assets = MinecraftVanillaDownloadPlanner.ParseAssets(index, gameDirectory, source);
                await GameLaunchService.VerifyAndDownloadAsync(assets, options, context, name);
                await Task.Run(() => MinecraftLaunchFiles.MaterializeLegacyAssetsAsync(index, gameDirectory, version.FolderPath, plan.AssetIndexId, context.CancellationToken), context.CancellationToken);
            })
        ], version.FolderPath);
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
