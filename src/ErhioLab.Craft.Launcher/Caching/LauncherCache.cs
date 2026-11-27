using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Caching;

internal sealed class LauncherCache(string dataDirectory)
{
    internal static LauncherCache Shared { get; } = new(Path.Combine(AppContext.BaseDirectory, "ELCL"));
    private const string RuntimeLockFile = ".runtime.lock";
    private static readonly string SessionPrefix = CurrentProcessStamp() + "-";

    private static readonly HashSet<string> LegacyWork = new(StringComparer.OrdinalIgnoreCase) { "launch", "loader-install", "modpack-download", "pack-export", "game-logs", "launch-patches" };

    private readonly object _metadataGate = new();

    internal string Root { get; } = Path.Combine(Path.GetFullPath(dataDirectory), "cache");
    internal string Catalog => Path.Combine(Root, "version_catalog.json");
    internal string Skins => Path.Combine(Root, "skins");
    internal string Capes => Path.Combine(Skins, "capes");
    internal string Community(string kind, string key, string extension) => Path.Combine(Root, "community", kind, Key(key).ToLowerInvariant() + extension);
    internal string VersionJson(Uri uri) => Path.Combine(Root, "version-json", Key(uri.AbsoluteUri) + ".json");
    internal static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal string Workspace(string kind)
    {
        VerifySegment(kind);
        string path = Path.Combine(Root, "work", kind, SessionPrefix + Guid.NewGuid().ToString("N"));
        if (CacheFileTree.IsLinked(path)) throw new IOException("不能使用链接指向的缓存工作目录.");
        return path;
    }

    internal CleanupResult RemoveWorkspace(string path, Action<string, Exception> onError)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        string name = Path.GetFileName(full);
        if (parent is null || !string.Equals(Path.GetDirectoryName(parent), Path.Combine(Root, "work"), StringComparison.OrdinalIgnoreCase) || !name.StartsWith(SessionPrefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[SessionPrefix.Length..], "N", out _))
            throw new IOException("拒绝清理不属于本次启动器会话的工作目录.");
        var tree = new CacheFileTree(onError);
        tree.Sweep(full, static _ => true);
        return tree.Result;
    }

    private static void VerifySegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.EndsWith('.') || value.EndsWith(' ') || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("缓存名称必须是单个有效的路径片段.", nameof(value));
    }

    internal static async Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        if (CacheFileTree.IsLinked(path)) throw new IOException("不能写入链接指向的缓存.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static void Touch(string path)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (File.GetLastAccessTimeUtc(path) < now.AddDays(-1)) File.SetLastAccessTimeUtc(path, now);
        }
        catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
        {
            /* 访问时间不会阻断缓存读取. */
        }
    }

    internal void MarkMetadataUsed(string path)
    {
        lock (_metadataGate)
        {
            if (CacheFileTree.IsLinked(path)) throw new IOException("不能使用链接指向的版本清单缓存.");
            Touch(path);
        }
    }

    internal async Task<RuntimeCacheLease> GetRuntimeAsync(string key, Func<string, CancellationToken, Task> prepare,
        Action<string, Exception> onError, CancellationToken token)
    {
        VerifySegment(key);
        using var guard = await LockRuntimeAsync(token).ConfigureAwait(false);
        string path = Path.Combine(Root, "runtime", key);
        string ready = Path.Combine(path, ".ready");
        if (CacheFileTree.IsLinked(ready)) throw new IOException("不能使用链接指向的启动缓存.");
        if (!RuntimeIsReady(path, ready))
        {
            if (Directory.Exists(path) && HasRuntimeUser(path)) throw new IOException("正在使用的启动缓存不完整.");
            string staging = Workspace("runtime");
            try
            {
                string payload = Path.Combine(staging, "files");
                Directory.CreateDirectory(payload);
                await prepare(payload, token).ConfigureAwait(false);
                var files = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
                    .Select(file => new CachedFile(Path.GetRelativePath(payload, file), new FileInfo(file).Length)).ToArray();
                await File.WriteAllBytesAsync(Path.Combine(staging, ".ready"), JsonSerializer.SerializeToUtf8Bytes(files), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(path)) new CacheFileTree(onError, token).Sweep(path, static _ => true);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Directory.Move(staging, path);
            }
            finally
            {
                RemoveWorkspace(staging, onError);
            }
        }

        Touch(ready);
        return new RuntimeCacheLease(path);
    }

    private static bool RuntimeIsReady(string root, string ready)
    {
        string payload = Path.Combine(root, "files");
        try
        {
            if (!File.Exists(ready) || !Directory.Exists(payload) || CacheFileTree.IsLinked(payload)) return false;
            var files = JsonSerializer.Deserialize<CachedFile[]>(File.ReadAllBytes(ready));
            if (files is null) return false;
            foreach (var file in files)
            {
                string full = Path.GetFullPath(Path.Combine(payload, file.Path));
                if (!full.StartsWith(payload + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || CacheFileTree.IsLinked(full) || !File.Exists(full) || new FileInfo(full).Length != file.Bytes) return false;
            }

            return true;
        }
        catch (Exception error) when (CacheFileTree.IsFileSystemError(error) || error is JsonException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<FileStream> LockRuntimeAsync(CancellationToken token)
    {
        string file = Path.Combine(Root, RuntimeLockFile);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (CacheFileTree.IsLinked(file)) throw new IOException("不能使用链接指向的启动缓存锁.");
            Directory.CreateDirectory(Root);
            try
            {
                return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }

    private sealed record CachedFile(string Path, long Bytes);

    internal CleanupResult Sweep(bool clearAll, DateTime now, Action<string, Exception> onError, CancellationToken token = default)
    {
        var tree = new CacheFileTree(onError, token);
        try
        {
            if (CacheFileTree.IsLinked(Root))
            {
                tree.Skip();
                return tree.Result;
            }

            if (!Directory.Exists(Root)) return tree.Result;
            var cutoff = now.AddDays(-30);
            var temporaryCutoff = now.AddDays(-2);
            var emptyCutoff = now.AddMinutes(-15);
            if (!clearAll) ReuseLegacyCapes(tree, token);
            foreach (string path in Directory.EnumerateFileSystemEntries(Root))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path).ToLowerInvariant();
                if (name == RuntimeLockFile) continue;
                if (name == "runtime")
                {
                    using var guard = LockRuntimeAsync(token).GetAwaiter().GetResult();
                    SweepRuntime(tree, path, clearAll, cutoff, token);
                    continue;
                }

                if (!clearAll && string.Equals(path, Catalog, StringComparison.OrdinalIgnoreCase)) continue;
                if (name == "launch" && HasJavaProcesses())
                {
                    tree.Skip();
                    continue;
                }

                if (name == "version-json")
                {
                    lock (_metadataGate)
                        tree.Sweep(path, file => clearAll || LastUsed(file) <= (file.Extension == ".tmp" ? temporaryCutoff : cutoff),
                            removeEmptyBefore: clearAll ? null : emptyCutoff);
                    continue;
                }

                bool temporary = name == "work" || LegacyWork.Contains(name);
                tree.Sweep(path, file => clearAll || LastUsed(file) <= (temporary || file.Extension == ".tmp" ? temporaryCutoff : cutoff),
                    protect: name == "work" ? entry => IsProcessAlive(Path.GetFileName(entry)) : null,
                    removeEmptyBefore: clearAll ? null : emptyCutoff);
            }

            if (!Directory.EnumerateFileSystemEntries(Root).Any()) Directory.Delete(Root);
        }
        catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
        {
            tree.Fail(Root, error);
        }

        return tree.Result;
    }

    private void SweepRuntime(CacheFileTree tree, string root, bool clearAll, DateTime cutoff, CancellationToken token)
    {
        if (CacheFileTree.IsLinked(root))
        {
            tree.Skip();
            return;
        }

        if (!Directory.Exists(root))
        {
            tree.Sweep(root, _ => clearAll);
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (CacheFileTree.IsLinked(directory) || HasRuntimeUser(directory))
            {
                tree.Skip();
                continue;
            }

            var ready = new FileInfo(Path.Combine(directory, ".ready"));
            if (clearAll || (ready.Exists ? LastUsed(ready) : Directory.GetLastWriteTimeUtc(directory)) <= cutoff)
                tree.Sweep(directory, static _ => true);
        }

        foreach (string file in Directory.EnumerateFiles(root)) tree.Sweep(file, entry => clearAll || LastUsed(entry) <= cutoff);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }

    private void ReuseLegacyCapes(CacheFileTree tree, CancellationToken token)
    {
        if (CacheFileTree.IsLinked(Capes) || !Directory.Exists(Capes)) return;
        foreach (string directory in Directory.EnumerateDirectories(Capes))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || CacheFileTree.IsLinked(directory)) continue;
            try
            {
                foreach (string old in Directory.EnumerateFiles(directory, "*.png"))
                {
                    token.ThrowIfCancellationRequested();
                    string key = Path.GetFileNameWithoutExtension(old);
                    if (key.Length != 64 || !key.All(Uri.IsHexDigit) || CacheFileTree.IsLinked(old)) continue;
                    string destination = Path.Combine(Capes, Path.GetFileName(old));
                    if (!File.Exists(destination))
                    {
                        try
                        {
                            File.Move(old, destination);
                        }
                        catch (IOException) when (File.Exists(destination))
                        {
                        }
                    }

                    // 重名时保留共享副本, 旧目录中的重复图片不再参与读取.
                    tree.Sweep(old, static _ => true);
                }

                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
            {
                tree.Fail(directory, error);
            }
        }
    }

    private static DateTime LastUsed(FileInfo file) => file.LastWriteTimeUtc >= file.LastAccessTimeUtc ? file.LastWriteTimeUtc : file.LastAccessTimeUtc;

    private static bool HasRuntimeUser(string path)
    {
        foreach (string marker in Directory.EnumerateFiles(path, ".use-*"))
        {
            if (CacheFileTree.IsLinked(marker)) return true;
            if (IsProcessAlive(Path.GetFileName(marker)[5..])) return true;
            File.Delete(marker);
        }

        return false;
    }

    private static string CurrentProcessStamp()
    {
        using var process = Process.GetCurrentProcess();
        return ProcessStamp(process);
    }

    private static string ProcessStamp(Process process) => FormattableString.Invariant($"{process.Id}-{process.StartTime.ToUniversalTime().Ticks}");

    private static bool IsProcessAlive(string stamp)
    {
        var parts = stamp.Split('-');
        if (
            parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var started) ||
            !Guid.TryParseExact(parts[2], "N", out _)
        ) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private static bool HasJavaProcesses()
    {
        foreach (string name in new[] { "java", "javaw" })
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes) process.Dispose();
            if (processes.Length > 0) return true;
        }

        return false;
    }

    internal sealed class RuntimeCacheLease : IDisposable
    {
        private readonly string _root;
        private readonly string _launcherMarker;
        private string? _gameMarker;
        internal string Path { get; }

        internal RuntimeCacheLease(string root)
        {
            _root = root;
            Path = System.IO.Path.Combine(root, "files");
            _launcherMarker = CreateMarker(SessionPrefix);
        }

        internal void Attach(Process process)
        {
            try
            {
                if (!process.HasExited) _gameMarker = CreateMarker(ProcessStamp(process) + "-");
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                /* 游戏进程在建立标记前已退出. */
            }
        }

        private string CreateMarker(string prefix)
        {
            string marker = System.IO.Path.Combine(_root, ".use-" + prefix + Guid.NewGuid().ToString("N"));
            if (CacheFileTree.IsLinked(marker)) throw new IOException("启动缓存的使用标记不能是链接.");
            using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            return marker;
        }

        public void Dispose()
        {
            if (_gameMarker is { } gameMarker) RemoveMarker(gameMarker);
            RemoveMarker(_launcherMarker);
        }

        private static void RemoveMarker(string marker)
        {
            try
            {
                File.Delete(marker);
            }
            catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
            {
                /* 下次扫描按进程标记回收. */
            }
        }
    }
}
