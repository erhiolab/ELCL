using System.IO;

namespace ErhioLab.Craft.Launcher.Caching;

internal sealed record CleanupResult(long Bytes, int Files, int Skipped, int Failed);

internal sealed class CacheFileTree(Action<string, Exception> onError, CancellationToken token = default)
{
    private long _bytes;
    private int _files, _skipped, _failed;
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    internal CleanupResult Result => new(_bytes, _files, _skipped, _failed);
    internal void Skip() => _skipped++;

    internal static bool IsFileSystemError(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    internal void Fail(string path, Exception error)
    {
        _failed++;
        onError(path, error);
    }

    internal static bool IsLinked(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
            }
        }

        return false;
    }

    internal void Sweep(string root, Func<FileInfo, bool> remove, Func<string, bool>? protect = null,
        DateTime? removeEmptyBefore = null)
    {
        root = Path.GetFullPath(root);
        try
        {
            if (IsLinked(root))
            {
                Skip();
                return;
            }
        }
        catch (Exception error) when (IsFileSystemError(error))
        {
            Fail(root, error);
            return;
        }

        var pending = new Stack<(string Path, bool VisitedChildren)>();
        pending.Push((root, false));
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (item.VisitedChildren)
                {
                    if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) == 0
                        && !Directory.EnumerateFileSystemEntries(item.Path).Any()) Directory.Delete(item.Path);
                    continue;
                }

                if (!_visited.Add(item.Path)) continue;
                var attributes = File.GetAttributes(item.Path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || protect?.Invoke(item.Path) == true)
                {
                    Skip();
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (removeEmptyBefore is null || Directory.GetLastWriteTimeUtc(item.Path) <= removeEmptyBefore)
                        pending.Push((item.Path, true));
                    foreach (var entry in Directory.EnumerateFileSystemEntries(item.Path)) pending.Push((entry, false));
                    continue;
                }

                var file = new FileInfo(item.Path);
                if (!remove(file)) continue;
                long bytes = file.Length;
                file.Delete();
                _bytes += bytes;
                _files++;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception error) when (IsFileSystemError(error))
            {
                Fail(item.Path, error);
            }
        }
    }
}
