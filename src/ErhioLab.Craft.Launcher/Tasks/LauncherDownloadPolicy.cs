using System.IO;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Tasks;

internal static class LauncherDownloadPolicy
{
    internal static DownloadOptions Capture(LauncherSettings? settings = null)
    {
        settings ??= App.Settings;
        return new DownloadOptions(settings.DownloadMaxConnections, (long)(settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024))
        {
            MaxRetries = settings.DownloadMaxRetries,
            HttpTimeoutSeconds = settings.DownloadHttpTimeoutSeconds,
            ForceRedownload = settings.ForceRedownload
        };
    }

    internal static Task<DownloadFileRequest> FindClientCandidatesAsync(DownloadFileRequest file, string gameDirectory,
        string gameVersion, DownloadOptions options, CancellationToken token)
    {
        if (options.ForceRedownload || file.ExpectedSha1 is null || file.Sources.Count == 0) return Task.FromResult(file);
        return Task.Run(() =>
        {
            var candidates = new List<string>();
            string versions = Path.Combine(Path.GetFullPath(gameDirectory), "versions");
            try
            {
                if (Directory.Exists(versions) && !CacheFileTree.IsLinked(versions))
                    foreach (string folder in Directory.EnumerateDirectories(versions))
                    {
                        token.ThrowIfCancellationRequested();
                        string name = Path.GetFileName(folder);
                        string path = Path.Combine(folder, name + ".jar");
                        try
                        {
                            if (string.Equals(path, file.DestinationPath, StringComparison.OrdinalIgnoreCase)
                                || !File.Exists(path) || CacheFileTree.IsLinked(folder) || CacheFileTree.IsLinked(path)
                                || (file.ExpectedSize is { } size && new FileInfo(path).Length != size)) continue;
                            if (name.Equals(gameVersion, StringComparison.OrdinalIgnoreCase)) candidates.Insert(0, path);
                            else candidates.Add(path);
                        }
                        catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
                        {
                            /* 单个候选不可读不影响下载. */
                        }
                    }
            }
            catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
            {
                App.Logger.Warning("Download", $"无法扫描客户端 JAR 复用候选, 将继续下载: {error.Message}");
            }

            return file with { ReusablePaths = candidates };
        }, token);
    }
}
