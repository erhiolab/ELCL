using System.IO;
using System.Security.Cryptography;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Mods;

namespace ErhioLab.Craft.Launcher.Tasks;

internal static class ModInstallTasks
{
    internal static LauncherTask Start(ModProject project, ModFile file, ModBrowserContext context, string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(CommunityMods.L("directoryMissing"));
        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, directory, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(CommunityMods.L("installBusy"));
        string work = Path.Combine(directory, ".elcl-mod-download-" + Guid.NewGuid().ToString("N"));
        ModDownloadItem[] plan = [new(project, file)];
        List<DownloadFileRequest> files = [];
        Dictionary<string, ModFile> downloads = [];
        var source = CommunityMods.Source;
        var options = new DownloadOptions(App.Settings.DownloadMaxConnections, (long)(App.Settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024));
        LauncherTaskStage Stage(string key, Func<LauncherTaskContext, Task> action) => new(CommunityMods.L(key), action) { LocalizedName = () => CommunityMods.L(key) };
        return App.Tasks.Start(string.Format(CommunityMods.L("installTask"), project.Name),
        [
            Stage("prepareFile", async stage =>
            {
                stage.CancellationToken.ThrowIfCancellationRequested();
                if (file.Platform != project.Platform || file.ProjectId != project.Id || !project.CanDownload || file.DownloadUri is null)
                    throw new InvalidDataException(CommunityMods.L("invalidFile"));
                if (context.GameVersion is not null && !file.GameVersions.Contains(context.GameVersion)
                    || context.Loader is not null && !file.Loaders.Contains(context.Loader, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException(CommunityMods.L("chooseCompatibility"));
                Directory.CreateDirectory(work);
                var existing = await Task.Run(() => LocalModManager.ScanAsync(directory, stage.CancellationToken), stage.CancellationToken);
                foreach (var item in plan)
                {
                    if (item.File.Sha1 is { } sha && existing.Any(local => local.Enabled && local.Sha1.Equals(sha, StringComparison.OrdinalIgnoreCase))) continue;
                    var translation = App.Settings.ModFileNameFormat == Settings.ModFileNameFormat.Original ? null : await CommunityMods.TranslationAsync(item.Project.Name, item.Project);
                    string name = CommunityMods.FileName(item.File, translation?.ChineseName);
                    string destination = Path.Combine(directory, name);
                    if (File.Exists(destination) || File.Exists(destination + ".disabled") || File.Exists(destination + ".disable")) throw new IOException(string.Format(CommunityMods.L("fileExists"), name));
                    if (downloads.ContainsKey(name)) throw new IOException(CommunityMods.L("duplicateNames"));
                    downloads[name] = item.File;
                    files.Add(new DownloadFileRequest(Path.Combine(work, name), ModRepositoryClient.DownloadSources(item.File.DownloadUri!, source), item.File.Size > 0 ? item.File.Size : null, item.File.Sha1));
                }

                stage.ConfigureStageWeights([1, Math.Max(1, files.Sum(LauncherTaskContext.EstimatedSize)), 1]);
            }),
            Stage("downloadFiles", stage =>
            {
                stage.ConfigureDownloadPlan(files, files);
                return files.Count == 0 ? Task.CompletedTask : stage.DownloadAsync(new DownloadTaskRequest(project.Name, files), options);
            }),
            Stage("installFiles", async stage =>
            {
                foreach (var request in files)
                {
                    var metadata = downloads[Path.GetFileName(request.DestinationPath)];
                    if (metadata.Sha512 is not { Length: > 0 } expected) continue;
                    await using var stream = File.OpenRead(request.DestinationPath);
                    string actual = Convert.ToHexStringLower(await SHA512.HashDataAsync(stream, stage.CancellationToken));
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(CommunityMods.L("checksumFailed"));
                }

                await LocalModManager.InstallAsync(directory, files.Select(item => item.DestinationPath).ToArray(), stage.CancellationToken);
                foreach (var item in plan)
                    if (item.File.Sha1 is { Length: > 0 } hash)
                        await CommunityMods.RememberProjectAsync(hash, item.Project);
                if (Directory.Exists(work)) Directory.Delete(work, true);
                App.Logger.Information("Mods", $"模组安装完成: {project.Name}, 文件={files.Count}, 目录={directory}.");
                CommunityMods.NotifyInstalled(directory);
            })
        ], directory, cleanupOnRemove: () =>
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
            return Task.CompletedTask;
        });
    }
}
