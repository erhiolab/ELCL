using System.IO;
using System.Net.Http;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Modpacks;
using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Tasks;

internal static class ModpackInstallTasks
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    internal static event Action<string>? Imported;

    internal static string CurrentGameDirectory => Path.GetFullPath(App.Settings.SelectedGameFolderPath ?? Path.Combine(AppContext.BaseDirectory, ".minecraft"));

    internal static LauncherTask Start(ModpackDefinition pack, string name, string gameDirectory) => StartCore(pack, null, name, gameDirectory);

    internal static LauncherTask StartOnline(ModFile file, string name, string gameDirectory)
    {
        CommunityArchiveTasks.ArchiveName(file, ModProjectKind.Modpack);
        return StartCore(null, file, name, gameDirectory);
    }

    private static LauncherTask StartCore(ModpackDefinition? localPack, ModFile? onlineFile, string name, string gameDirectory)
    {
        MinecraftVanillaDownloadPlanner.ValidateVersionName(name);
        gameDirectory = Path.GetFullPath(gameDirectory);
        string directory = Path.Combine(gameDirectory, "versions", name);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException(LocalizationManager.Get("install.versionExists"));
        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, directory, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(LocalizationManager.Get("install.alreadyRunning"));
        LauncherSettings settings = App.Settings;
        var source = settings.FileDownloadSource == VersionListSource.MirrorFirst ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
        var community = settings.CommunityDownloadSource == VersionListSource.MirrorFirst ? CommunitySource.MirrorFirst : CommunitySource.OfficialFirst;
        var options = new DownloadOptions(settings.DownloadMaxConnections, (long)(settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024));
        IReadOnlyList<ModpackDownload> downloads = [];
        MinecraftInstallOperation? operation = null;
        bool ownsDirectory = false;

        LauncherTaskStage Stage(string key, Func<LauncherTaskContext, Task> action, long bytes = 1) => new(LocalizationManager.Get(key), action)
            { LocalizedName = () => LocalizationManager.Get(key), PlannedBytes = Math.Max(1, bytes) };

        ModpackDefinition? pack = localPack;
        string? workingDirectory = onlineFile is null ? null : LauncherCache.Shared.Workspace("modpack-download");
        List<LauncherTaskStage> stages = [];
        if (onlineFile is not null)
        {
            string archive = Path.Combine(workingDirectory!, onlineFile.FileName);
            stages.Add(Stage("community.downloadArchive", async context =>
            {
                await CommunityArchiveTasks.DownloadAsync(onlineFile, archive, context, options, community);
                pack = await ModpackArchive.ReadAsync(archive, context.CancellationToken);
            }, onlineFile.Size));
        }

        stages.Add(Stage("modpack.prepare", async context =>
        {
            var definition = pack ?? throw new InvalidDataException(CommunityResourceCatalog.L("invalidArchive"));
            App.Logger.Information("Modpacks", $"准备整合包: 格式={definition.Format}, 游戏={definition.GameVersion}, 名称={name}.");
            var preferred = settings.VersionListSource == VersionListSource.MirrorFirst ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
            var version = await LauncherVersionCatalogCache.FindAsync(LauncherCache.Shared, new MinecraftVersionCatalogClient(Http), definition.GameVersion, preferred,
                error => App.Logger.Warning("Cache", $"整合包安装版本列表缓存读取或刷新失败: {error.Message}"), context.CancellationToken)
                ?? throw new InvalidDataException(string.Format(LocalizationManager.Get("modpack.gameUnavailable"), definition.GameVersion));
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException(LocalizationManager.Get("install.versionExists"));
            operation = App.Tasks.CreateMinecraftInstall(version, name, gameDirectory, source, options, definition.Loaders, context.Owner);
            string reservation = Path.Combine(gameDirectory, "versions", ".elcl-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reservation);
            try
            {
                // Move 对已存在的目标失败, 避免把别的操作刚创建的版本当成本任务所有.
                Directory.Move(reservation, directory);
                ownsDirectory = true;
            }
            finally
            {
                if (Directory.Exists(reservation)) Directory.Delete(reservation);
            }

            context.AppendStages([
                .. operation.Stages,
                Stage("modpack.resolve", async stage => { downloads = await ModpackArchive.ResolveDownloadsAsync(definition, directory, Http, ServiceConfiguration.CurseForgeApiKey, community, stage.CancellationToken); }),
                Stage("modpack.download", async stage =>
                {
                    var files = downloads.Select(item => item.Request).ToArray();
                    var missing = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(files, stage.CancellationToken);
                    stage.ConfigureDownloadPlan(files, missing);
                    if (missing.Count > 0) await stage.DownloadAsync(new DownloadTaskRequest(name, missing), options);
                    await ModpackArchive.VerifyDownloadsAsync(downloads, stage.CancellationToken);
                }, definition.Files.Sum(file => file.Size ?? 1024 * 1024)),
                Stage("modpack.overrides", async stage => { await ModpackArchive.ExtractOverridesAsync(definition, directory, new Progress<double>(stage.ReportProgress), stage.CancellationToken); }, definition.Overrides.Sum(file => file.Size))
            ]);
        }));

        void CleanupWorkingArchive()
        {
            if (workingDirectory is not null)
                LauncherCache.Shared.RemoveWorkspace(workingDirectory,
                    (entry, error) => App.Logger.Error("Cache", $"无法清理整合包下载缓存: {entry}.", error));
        }

        async Task CleanupAsync()
        {
            try
            {
                if (!ownsDirectory) return;
                if (operation is not null) await operation.Cleanup();
                string expected = Path.GetFullPath(Path.Combine(gameDirectory, "versions")) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(directory).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("整合包清理目录超出版本目录.");
                if (Directory.Exists(directory)) await Task.Run(() => Directory.Delete(directory, true));
                await LauncherProfileCompatibility.RemoveVersionAsync(gameDirectory, directory);
                ownsDirectory = false;
            }
            finally { CleanupWorkingArchive(); }
        }

        LauncherTask task = App.Tasks.Start(string.Format(LocalizationManager.Get("modpack.task"), name), stages, directory,
            cleanupOnRemove: CleanupAsync, cleanupOnFailure: CleanupAsync, onCompleted: () =>
            {
                try { CleanupWorkingArchive(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    App.Logger.Error("Modpacks", "整合包已安装, 无法移除下载缓存.", error);
                }
                Imported?.Invoke(directory);
            });
        task.LocalizedTitle = () => string.Format(LocalizationManager.Get("modpack.task"), name);
        App.Logger.Information("Modpacks", $"创建整合包安装任务: 名称={name}, 文件={localPack?.ArchivePath ?? onlineFile?.FileName}, 目录={directory}.");
        return task;
    }
}
