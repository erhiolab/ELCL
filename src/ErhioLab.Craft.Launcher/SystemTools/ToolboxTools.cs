using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using ErhioLab.Craft.Launcher.Caching;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal static class ToolboxTools
{
    // 分数只依赖本地日期; 文案在当天首次抽取后由调用方保存在配置中.
    internal static int DailyLuck(DateOnly date) => (int)(BitConverter.ToUInt32(
        SHA256.HashData(Encoding.UTF8.GetBytes("ELCL.daily-luck/" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))), 0) % 101);

    internal static string DailyLuckMessageKey(int score, string? savedKey = null)
    {
        string range = Math.Clamp(score, 0, 100) switch
        {
            0 => "zero", <= 19 => "gentle", <= 39 => "warmup", <= 59 => "steady",
            <= 79 => "sunny", < 100 => "sparkly", _ => "perfect"
        };
        string prefix = $"toolbox.luck.{range}.";
        if (savedKey is not null && Enumerable.Range(1, 6).Any(index => savedKey == prefix + index))
            return savedKey;
        return prefix + Random.Shared.Next(1, 7);
    }

    internal static CleanupResult CleanGameFiles(IReadOnlyList<string> gameDirectories, string launcherDataDirectory,
        Action<string, Exception> onError, CancellationToken token = default)
    {
        var tree = new CacheFileTree(onError, token);
        string data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(launcherDataDirectory));
        string[] garbageDirectories = ["logs", "crash-reports", "cache", ".cache", "server-resource-packs"];
        foreach (var game in gameDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(game));
                var versions = Path.Combine(root, "versions");
                if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)
                    || Within(root, data) || CacheFileTree.IsLinked(root)) { tree.Skip(); continue; }
                if (!Directory.Exists(root)) continue;
                if (!Directory.Exists(versions) && !File.Exists(Path.Combine(root, "launcher_profiles.json"))
                    && !Path.GetFileName(root).Equals(".minecraft", StringComparison.OrdinalIgnoreCase)) { tree.Skip(); continue; }

                CleanDirectory(root);
                if (CacheFileTree.IsLinked(versions)) { tree.Skip(); continue; }
                if (!Directory.Exists(versions)) continue;
                foreach (var version in Directory.EnumerateDirectories(versions)) CleanDirectory(version);
            }
            catch (Exception error) when (CacheFileTree.IsFileSystemError(error) || error is ArgumentException or NotSupportedException)
            {
                tree.Fail(game, error);
            }
        }
        return tree.Result;

        void CleanDirectory(string directory)
        {
            foreach (var name in garbageDirectories)
            {
                string path = Path.Combine(directory, name);
                if (Within(path, data) || Within(data, path))
                {
                    tree.Skip();
                    continue;
                }
                tree.Sweep(path, static _ => true);
            }
        }
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
