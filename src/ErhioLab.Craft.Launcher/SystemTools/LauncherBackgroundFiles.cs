using System.IO;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal sealed record LauncherBackgroundFile(string Path, string RelativePath, bool IsVideo);

internal static class LauncherBackgroundFiles
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".wmv", ".avi" };

    internal static string DirectoryPath { get; } = Path.Combine(AppContext.BaseDirectory, "ELCL", "backgrounds");

    internal static string EnsureDirectory(string directory)
    {
        var folder = Directory.CreateDirectory(directory);
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("背景目录不能是链接.");
        return folder.FullName;
    }

    internal static LauncherBackgroundFile[] Scan(string directory)
    {
        var folder = new DirectoryInfo(directory);
        if (!folder.Exists) return [];
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("背景目录不能是链接.");

        return folder.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            .Where(file => IsSupported(file.Extension))
            .Select(file => new LauncherBackgroundFile(file.FullName, Path.GetRelativePath(folder.FullName, file.FullName),
                VideoExtensions.Contains(file.Extension)))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path)) return null;
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.All(segment => segment.Length is > 0 and <= 255 && segment[^1] is not ('.' or ' ') && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            ? string.Join(Path.DirectorySeparatorChar, segments)
            : null;
    }

    internal static bool TryDelete(string directory, LauncherBackgroundFile file)
    {
        var folder = new DirectoryInfo(directory);
        var candidate = new FileInfo(file.Path);
        var relative = NormalizeRelativePath(Path.GetRelativePath(folder.FullName, candidate.FullName));
        if (relative is null || !string.Equals(relative, file.RelativePath, StringComparison.OrdinalIgnoreCase) || !IsSupported(candidate.Extension)) throw new IOException("文件不在可清理的背景目录中.");

        // 刷新后目录可能已被替换成链接, 删除前重新检查背景根目录和所有中间目录.
        var parent = folder;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar).SkipLast(1))
        {
            if (!parent.Exists) return false;
            RejectLink(parent);
            parent = new DirectoryInfo(Path.Combine(parent.FullName, segment));
        }

        if (!parent.Exists) return false;
        RejectLink(parent);
        if (!candidate.Exists) return false;
        RejectLink(candidate);

        candidate.Delete();
        return true;
    }

    private static bool IsSupported(string extension) => ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension);

    private static void RejectLink(FileSystemInfo entry)
    {
        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"不能清理链接或通过链接访问的背景: {entry.FullName}.");
    }
}
