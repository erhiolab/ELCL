using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal sealed record CleanupResult(long Bytes, int Files, int Skipped, int Failed);

internal static class ToolboxTools
{
    // 只依赖本地日期, 不保存随机结果; 同一天重复点击结果相同.
    internal static int DailyLuck(DateOnly date) => (int)(BitConverter.ToUInt32(
        SHA256.HashData(Encoding.UTF8.GetBytes("ELCL.daily-luck/" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))), 0) % 101);

    internal static string DailyLuckMessageKey(int score)
    {
        string range = Math.Clamp(score, 0, 100) switch
        {
            0 => "zero", <= 19 => "gentle", <= 39 => "warmup", <= 59 => "steady",
            <= 79 => "sunny", < 100 => "sparkly", _ => "perfect"
        };
        return $"toolbox.luck.{range}.{Random.Shared.Next(1, 7)}";
    }

    internal static CleanupResult Clean(IReadOnlyList<string> gameDirectories, string launcherDataDirectory,
        IReadOnlyCollection<string> activePaths, Action<string, Exception> onError)
    {
        var roots = new List<string>();
        var data = Path.GetFullPath(launcherDataDirectory);
        roots.Add(Path.Combine(data, "cache"));
        roots.Add(Path.Combine(data, "logs"));
        string[] garbageDirectories = ["logs", "crash-reports", "cache", ".cache", "server-resource-packs"];
        foreach (var game in gameDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var root = Path.GetFullPath(game);
            if (IsLink(root)) continue;
            foreach (var name in garbageDirectories) roots.Add(Path.Combine(root, name));
            var versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions) || IsLink(versions)) continue;
            try
            {
                foreach (var version in Directory.EnumerateDirectories(versions))
                {
                    if (IsLink(version)) continue;
                    foreach (var name in garbageDirectories) roots.Add(Path.Combine(version, name));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                onError(versions, error);
            }
        }

        long bytes = 0;
        int files = 0, skipped = 0, failed = 0;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                if (!visited.Add(directory) || !Directory.Exists(directory)) continue;
                try
                {
                    if (IsLink(directory) || activePaths.Any(path => Within(directory, path)))
                    {
                        skipped++;
                        continue;
                    }

                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        var attributes = File.GetAttributes(entry);
                        if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            skipped++;
                            continue;
                        }

                        if (attributes.HasFlag(FileAttributes.Directory))
                        {
                            pending.Push(entry);
                            continue;
                        }

                        if (string.Equals(entry, Path.Combine(data, "logs", "Log1.txt"), StringComparison.OrdinalIgnoreCase)
                            || activePaths.Any(path => Within(entry, path)))
                        {
                            skipped++;
                            continue;
                        }

                        try
                        {
                            var info = new FileInfo(entry);
                            var length = info.Length;
                            info.Delete();
                            bytes += length;
                            files++;
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            failed++;
                            onError(entry, error);
                        }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    onError(directory, error);
                }
            }
        }

        return new CleanupResult(bytes, files, skipped, failed);
    }

    private static bool IsLink(string path) => Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static bool Within(string path, string root) => string.Equals(Path.GetFullPath(path), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                                                            || Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
