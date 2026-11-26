using System.IO.Compression;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public static partial class LocalMinecraftWorldManager
{
    private const long ImportZipSizeLimit = 64L * 1024 * 1024 * 1024;

    /// <summary>修改世界的显示名称, 不更改目录或备份.</summary>
    public static Task RenameAsync(string worldDirectory, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        if (name.Length is 0 or > 128 || name.Any(char.IsControl))
            throw new ArgumentException("世界名称必须为 1 到 128 个字符, 且不能包含控制字符.", nameof(name));
        return Task.Run(() => EditLevelDat(Full(worldDirectory), path => MinecraftWorldMetadata.OpenNameEdit(path, name, cancellationToken),
            cancellationToken), cancellationToken);
    }

    /// <summary>从世界目录或 ZIP 导入独立副本, 返回新世界目录的完整路径.</summary>
    public static Task<string> ImportAsync(string savesDirectory, string sourcePath, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        string root = Full(savesDirectory), source = Full(sourcePath);
        EnsureNoLinks(root);
        EnsureNoLinks(source);
        bool isFolder = Directory.Exists(source);
        if (isFolder && IsWithin(source, root)) throw new ArgumentException("世界来源目录不能包含目标世界目录.", nameof(sourcePath));
        if (!isFolder && !string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择世界目录或 ZIP 文件.", nameof(sourcePath));

        Directory.CreateDirectory(root);
        string staging = Temporary(root);
        try
        {
            Directory.CreateDirectory(staging);
            string world = staging;
            if (isFolder)
            {
                using var worldLock = CheckWorldStopped(source);
                if (ReadMetadata(source, cancellationToken).MetadataError is { } sourceError)
                    throw new InvalidDataException("无法读取所选目录的世界信息: " + sourceError);
                await CopyWorldForImportAsync(source, staging, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ExtractAsync(source, staging, cancellationToken, ImportZipSizeLimit).ConfigureAwait(false);
                world = FindImportedWorld(staging);
            }

            var metadata = ReadMetadata(world, cancellationToken);
            if (metadata.MetadataError is { } error) throw new InvalidDataException("无法读取导入的世界信息: " + error);
            // 会话锁只属于原有实例, 不复制进新的世界.
            File.Delete(Path.Combine(world, "session.lock"));
            string preferredName = isFolder ? Path.GetFileName(source)
                : !string.Equals(world, staging, PathComparison) ? Path.GetFileName(world)
                : metadata.Name == Path.GetFileName(staging) ? Path.GetFileNameWithoutExtension(source) : metadata.Name;
            string destination = ChooseImportDestination(root, preferredName);

            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoLinks(root);
            Directory.Move(world, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) RemoveDetached(staging);
        }
    }, cancellationToken);

    private static string FindImportedWorld(string staging)
    {
        static bool HasMetadata(string directory) => File.Exists(Path.Combine(directory, "level.dat")) || File.Exists(Path.Combine(directory, "level.dat_old"));
        var candidates = Directory.EnumerateDirectories(staging).Prepend(staging).Where(HasMetadata).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : throw new InvalidDataException("ZIP 必须包含一个 Java 版世界, 世界文件应位于 ZIP 根目录或一层子目录.");
    }

    private static string ChooseImportDestination(string root, string preferredName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        int length = Math.Min(preferredName.Length, 80);
        if (length < preferredName.Length && char.IsHighSurrogate(preferredName[length - 1])) length--;
        string name = new string(preferredName[..length].Select(c => c is '/' or '\\' or ':' || char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        try
        {
            _ = WorldPath(root, name);
        }
        catch (ArgumentException)
        {
            name = "World";
        }

        for (int i = 1; i <= 10_000; i++)
        {
            string candidate = WorldPath(root, i == 1 ? name : FormattableString.Invariant($"{name} ({i})"));
            if (!Directory.Exists(candidate) && !File.Exists(candidate) && !IsLink(candidate)) return candidate;
        }

        throw new IOException("找不到可用于导入的世界目录名称.");
    }

    private static async Task CopyWorldForImportAsync(string source, string destination, CancellationToken token)
    {
        int count = 0;
        foreach (var item in EnumerateTree(source, token, skipLinks: false))
        {
            if (string.Equals(item.Path, Path.Combine(source, "session.lock"), PathComparison)) continue;
            if (++count > EntryLimit) throw new InvalidDataException("世界包含的文件和目录过多.");
            string target = Path.Combine(destination, Path.GetRelativePath(source, item.Path));
            if (item.Directory) Directory.CreateDirectory(target);
            else
            {
                await using var input = File.OpenRead(item.Path);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
        }
    }

    private static void EditLevelDat(string world, Func<string, Action<Stream>?> prepareEdit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string path = Path.Combine(world, "level.dat");
        EnsureNoLinks(path);
        using var worldLock = CheckWorldStopped(world);
        var original = new FileInfo(path);
        long length = original.Length;
        DateTime modified = original.LastWriteTimeUtc;
        if (prepareEdit(path) is not { } write) return;
        string temporary = Temporary(world) + ".part";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                write(gzip);
            token.ThrowIfCancellationRequested();
            EnsureNoLinks(path);
            original.Refresh();
            if (original.Length != length || original.LastWriteTimeUtc != modified)
                throw new IOException("世界信息已经变化, 请刷新后再试.");
            File.Replace(temporary, path, null);
        }
        finally
        {
            EnsureNoLinks(temporary);
            File.Delete(temporary);
        }
    }
}
