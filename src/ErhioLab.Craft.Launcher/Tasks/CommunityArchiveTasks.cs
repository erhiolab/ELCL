using System.IO;
using System.Security.Cryptography;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Mods;

namespace ErhioLab.Craft.Launcher.Tasks;

internal static class CommunityArchiveTasks
{
    internal static bool CanDownload(ModFile file, ModProjectKind kind) => file.DownloadUri is not null && (kind switch
    {
        ModProjectKind.Mod => true,
        ModProjectKind.Modpack => Path.GetExtension(file.FileName).ToLowerInvariant() is ".zip" or ".mrpack",
        _ => Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase)
    });

    internal static string ArchiveName(ModFile file, ModProjectKind kind)
    {
        string name = file.FileName;
        if (!CanDownload(file, kind) || string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException(CommunityResourceCatalog.L("invalidArchive"));
        return name;
    }

    internal static async Task DownloadAsync(ModFile file, string path, LauncherTaskContext context, DownloadOptions options, CommunitySource source)
    {
        var request = new DownloadFileRequest(path, ModRepositoryClient.DownloadSources(file.DownloadUri!, source),
            file.Size > 0 ? file.Size : null, file.Sha1);
        await context.DownloadAsync(new DownloadTaskRequest(file.FileName, [request]), options);
        if (!string.IsNullOrEmpty(file.Sha512))
        {
            await using var input = File.OpenRead(path);
            var sha512 = await SHA512.HashDataAsync(input, context.CancellationToken);
            if (!Convert.ToHexString(sha512).Equals(file.Sha512, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(CommunityMods.L("checksumFailed"));
        }
    }

    internal static LauncherTask Start(ModProject project, ModFile file, string destination)
    {
        ArchiveName(file, project.Kind);
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException(CommunityResourceCatalog.L("fileExists"));
        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, destination, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(CommunityResourceCatalog.L("downloadBusy"));
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".elcl-resource-" + Guid.NewGuid().ToString("N"));
        var options = new DownloadOptions(App.Settings.DownloadMaxConnections, (long)(App.Settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024));
        var source = CommunityMods.Source;
        string Title() => string.Format(CommunityResourceCatalog.L("task"), project.Name);
        string StageName() => CommunityResourceCatalog.L("downloadArchive");
        var stage = new LauncherTaskStage(StageName(), async context =>
        {
            await DownloadAsync(file, temporary, context, options, source);
            context.CancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }) { LocalizedName = StageName, PlannedBytes = Math.Max(1, file.Size) };
        var task = App.Tasks.Start(Title(), [stage], destination, cleanupOnFailure: () =>
        {
            File.Delete(temporary);
            return Task.CompletedTask;
        });
        task.LocalizedTitle = Title;
        App.Logger.Information("Mods", $"创建社区资源下载任务: 类型={project.Kind}, 项目={project.Name}, 目标={destination}.");
        return task;
    }
}
