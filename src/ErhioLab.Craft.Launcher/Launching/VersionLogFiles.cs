using System.IO;
using System.IO.Compression;

namespace ErhioLab.Craft.Launcher.Launching;

internal sealed record VersionLogFile(string Path, string Name, DateTime Modified, long Size, int? EntryIndex = null, string? EntryName = null)
{
    internal bool IsCompressed => EntryIndex.HasValue || System.IO.Path.GetExtension(Path).Equals(".gz", StringComparison.OrdinalIgnoreCase);
}

/// <summary>枚举版本日志, 按需只解压一个成员到自有临时目录, 不采用压缩包中的输出路径.</summary>
internal static class VersionLogFiles
{
    internal const long MaximumExtractedBytes = 512L * 1024 * 1024;

    internal static IReadOnlyList<VersionLogFile> Scan(string directory, CancellationToken token)
    {
        if (!Directory.Exists(directory)) return [];
        List<VersionLogFile> result = [];
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            token.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".log" or ".txt" or ".gz")
                result.Add(new VersionLogFile(path, file.Name, file.LastWriteTime, file.Length));
            else if (extension == ".zip")
            {
                int before = result.Count;
                try
                {
                    using var input = OpenShared(path);
                    using var archive = new ZipArchive(input, ZipArchiveMode.Read);
                    for (int index = 0; index < archive.Entries.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        var entry = archive.Entries[index];
                        if (entry.Name.Length == 0 || !IsTextLog(entry.Name)) continue;
                        result.Add(new VersionLogFile(path, file.Name + " / " + entry.FullName, file.LastWriteTime, entry.Length, index, entry.FullName));
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // 损坏或无法读取的压缩包也显示, 打开时提供明确错误.
                    result.RemoveRange(before, result.Count - before);
                }

                if (result.Count == before) result.Add(new VersionLogFile(path, file.Name, file.LastWriteTime, file.Length));
            }
        }

        return result.OrderByDescending(file => file.Modified).ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static async Task<OpenedVersionLog> OpenAsync(VersionLogFile file, string cacheRoot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!file.IsCompressed)
        {
            if (!IsTextLog(file.Path)) throw new InvalidDataException("压缩包中没有可读取的日志文件.");
            await using var check = OpenShared(file.Path);
            return new OpenedVersionLog(file.Path);
        }

        string directory = Path.Combine(Path.GetFullPath(cacheRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var opened = new OpenedVersionLog(Path.Combine(directory, "log.txt"), directory);
        try
        {
            await using var input = OpenShared(file.Path);
            if (file.EntryIndex is { } index)
            {
                await using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
                if (index >= archive.Entries.Count || archive.Entries[index].FullName != file.EntryName)
                    throw new InvalidDataException("压缩包内容已更改, 请刷新列表.");
                var entry = archive.Entries[index];
                if (entry.Length > MaximumExtractedBytes) throw new InvalidDataException("解压后的日志超过 512 MiB.");
                await using var source = await entry.OpenAsync(token).ConfigureAwait(false);
                await ExtractAsync(source, opened.Path, token).ConfigureAwait(false);
            }
            else
            {
                await using var source = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
                await ExtractAsync(source, opened.Path, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            return opened;
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    private static bool IsTextLog(string name) => Path.GetExtension(name).ToLowerInvariant() is ".log" or ".txt";
    private static FileStream OpenShared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static async Task ExtractAsync(Stream input, string path, CancellationToken token)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        byte[] buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaximumExtractedBytes) throw new InvalidDataException("解压后的日志超过 512 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }
}

internal sealed class OpenedVersionLog(string path, string? temporaryDirectory = null) : IDisposable
{
    internal string Path { get; } = path;
    internal bool IsTemporary => temporaryDirectory is not null;

    public void Dispose()
    {
        if (temporaryDirectory is null) return;
        // 只删除本次创建的固定文件与空目录, 不递归删除或使用压缩包中的路径.
        File.Delete(Path);
        if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory);
    }
}
