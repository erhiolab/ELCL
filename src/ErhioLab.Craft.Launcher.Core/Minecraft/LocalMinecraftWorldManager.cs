using System.Globalization;
using System.IO.Compression;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>扫描和管理调用方指定目录中的 Java 版世界及其备份.</summary>
public static partial class LocalMinecraftWorldManager
{
    private const string WorkingPrefix = ".elcl-world-op-";
    private const int IconLimit = 4 * 1024 * 1024;
    private const int EntryLimit = 500_000;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static Task<IReadOnlyList<LocalMinecraftWorld>> ScanAsync(string savesDirectory, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<LocalMinecraftWorld>>(() =>
    {
        string root = Full(savesDirectory);
        EnsureNoLinks(root);
        if (!Directory.Exists(root)) return [];
        List<LocalMinecraftWorld> worlds = [];
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(directory).StartsWith(WorkingPrefix, PathComparison) || IsLink(directory) || !LooksLikeWorld(directory)) continue;
            worlds.Add(ReadWorld(directory, cancellationToken));
        }

        return worlds.OrderByDescending(world => world.LastPlayed).ThenBy(world => world.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, cancellationToken);

    public static Task<IReadOnlyList<MinecraftWorldBackup>> ListBackupsAsync(string backupsDirectory, string worldDirectoryName,
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<MinecraftWorldBackup>>(() =>
    {
        string directory = WorldPath(Full(backupsDirectory), worldDirectoryName);
        EnsureNoLinks(directory);
        if (!Directory.Exists(directory)) return [];
        List<MinecraftWorldBackup> backups = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.zip"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsLink(path)) backups.Add(ToBackup(path));
        }

        return backups.OrderByDescending(backup => backup.CreatedAt).ThenByDescending(backup => backup.FileName, StringComparer.Ordinal).ToArray();
    }, cancellationToken);

    public static Task<MinecraftWorldBackup> BackupAsync(string savesDirectory, string backupsDirectory, string worldDirectoryName,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        var (world, backups) = Resolve(savesDirectory, backupsDirectory, worldDirectoryName);
        using var worldLock = CheckWorldStopped(world);
        Directory.CreateDirectory(backups);
        EnsureNoLinks(backups);
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        string output = Path.Combine(backups, stamp + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        string temporary = Temporary(backups) + ".part";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                int count = 0;
                foreach (var item in EnumerateTree(world, cancellationToken, skipLinks: false))
                {
                    if (string.Equals(item.Path, Path.Combine(world, "session.lock"), PathComparison)) continue;
                    if (++count > EntryLimit) throw new InvalidDataException("世界包含的文件和目录过多.");
                    string relative = Path.GetRelativePath(world, item.Path).Replace(Path.DirectorySeparatorChar, '/');
                    if (item.Directory)
                    {
                        archive.CreateEntry(relative + "/");
                        continue;
                    }

                    var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                    await using var source = File.OpenRead(item.Path);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output);
            return ToBackup(output);
        }
        finally
        {
            EnsureNoLinks(temporary);
            File.Delete(temporary);
        }
    }, cancellationToken);

    public static Task DeleteBackupAsync(string backupsDirectory, string worldDirectoryName, string backupFileName,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        string source = ExistingBackup(WorldPath(Full(backupsDirectory), worldDirectoryName), backupFileName);
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(source);
    }, cancellationToken);

    public static Task ExportBackupAsync(string backupsDirectory, string worldDirectoryName, string backupFileName, string destinationPath,
        bool overwrite = false, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        string root = Full(backupsDirectory);
        string source = ExistingBackup(WorldPath(root, worldDirectoryName), backupFileName);
        string destination = Full(destinationPath);
        if (IsWithin(root, destination)) throw new ArgumentException("请将备份导出到备份目录之外.", nameof(destinationPath));
        EnsureNoLinks(destination);
        if (!overwrite && File.Exists(destination)) throw new IOException("目标文件已存在: " + destination);
        string directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("请选择目标文件路径.", nameof(destinationPath));
        string temporary = Temporary(directory) + ".part";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoLinks(destination);
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            EnsureNoLinks(temporary);
            File.Delete(temporary);
        }
    }, cancellationToken);

    public static Task RestoreAsync(string savesDirectory, string backupsDirectory, string worldDirectoryName, string backupFileName,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        var (world, backups) = Resolve(savesDirectory, backupsDirectory, worldDirectoryName);
        string archivePath = ExistingBackup(backups, backupFileName);
        string root = Path.GetDirectoryName(world)!;
        string staging = Temporary(root);
        string previous = Temporary(root);
        using (CheckWorldStopped(world))
        {
        }

        try
        {
            Directory.CreateDirectory(staging);
            await ExtractAsync(archivePath, staging, cancellationToken).ConfigureAwait(false);
            if (ReadMetadata(staging, cancellationToken).MetadataError is { } error)
                throw new InvalidDataException("无法读取备份中的世界存档: " + error);
            EnsureNoLinks(world);
            ValidateTree(world, cancellationToken);
            using (CheckWorldStopped(world))
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            // 在同一目录下交换存档. 解压失败或无法就位时保留原存档.
            Directory.Move(world, previous);
            try
            {
                Directory.Move(staging, world);
            }
            catch
            {
                Directory.Move(previous, world);
                throw;
            }

            RemoveDetached(previous);
        }
        finally
        {
            if (Directory.Exists(staging)) RemoveDetached(staging);
        }
    }, cancellationToken);

    public static Task DeleteAsync(string savesDirectory, string backupsDirectory, string worldDirectoryName,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var (world, backups) = Resolve(savesDirectory, backupsDirectory, worldDirectoryName);
        ValidateTree(world, cancellationToken);
        bool hasBackups = Directory.Exists(backups);
        if (hasBackups) ValidateTree(backups, cancellationToken);
        using (CheckWorldStopped(world))
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        string oldWorld = Temporary(Path.GetDirectoryName(world)!);
        string oldBackups = Temporary(Path.GetDirectoryName(backups)!);
        Directory.Move(world, oldWorld);
        try
        {
            if (hasBackups) Directory.Move(backups, oldBackups);
        }
        catch
        {
            Directory.Move(oldWorld, world);
            throw;
        }

        RemoveDetached(oldWorld);
        if (hasBackups) RemoveDetached(oldBackups);
    }, cancellationToken);

    private static LocalMinecraftWorld ReadMetadata(string directory, CancellationToken token)
    {
        LocalMinecraftWorld world = new(directory, Path.GetFileName(directory));
        string? errorMessage = null;
        foreach (string name in new[] { "level.dat", "level.dat_old" })
        {
            try
            {
                string path = Path.Combine(directory, name);
                if (!File.Exists(path)) continue;
                EnsureNoLinks(path);
                return MinecraftWorldMetadata.Read(path, world, token) with { UsedPreviousMetadata = name == "level.dat_old" };
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                errorMessage ??= error.Message;
            }
        }

        return world with { MetadataError = errorMessage ?? "找不到世界元数据文件." };
    }

    private static LocalMinecraftWorld ReadWorld(string directory, CancellationToken token)
    {
        var world = ReadMetadata(directory, token);
        string settingsPath = Path.Combine(directory, "data", "minecraft", "world_gen_settings.dat");
        try
        {
            // 26.1 起种子在独立数据文件中, 不依赖存档显示名称或版本号猜测格式.
            if (File.Exists(settingsPath))
            {
                EnsureNoLinks(settingsPath);
                if (MinecraftWorldMetadata.ReadSeed(settingsPath, token) is { } seed) world = world with { Seed = seed };
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }

        long? size = null;
        byte[]? icon = null;
        try
        {
            long total = 0;
            foreach (var item in EnumerateTree(directory, token, skipLinks: true))
                if (!item.Directory)
                    total = checked(total + new FileInfo(item.Path).Length);
            size = total;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException)
        {
        }

        try
        {
            string iconPath = Path.Combine(directory, "icon.png");
            if (File.Exists(iconPath) && !IsLink(iconPath))
            {
                using var input = File.OpenRead(iconPath);
                if (input.Length is > 0 and <= IconLimit)
                {
                    byte[] bytes = new byte[(int)input.Length];
                    input.ReadExactly(bytes);
                    icon = bytes;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        return world with { Size = size, Icon = icon };
    }

    private static async Task ExtractAsync(string source, string destination, CancellationToken token, long? sizeLimit = null)
    {
        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count > EntryLimit) throw new InvalidDataException("备份 ZIP 条目过多.");
        if (sizeLimit is { } remaining)
        {
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Length < 0 || entry.Length > remaining) throw new InvalidDataException("ZIP 解压总大小超过 64 GiB, 无法导入.");
                remaining -= entry.Length;
            }
        }
        byte[] buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            // 拒绝符号链接, 并逐级验证文件名, 不允许 ZIP 写到暂存目录外.
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("备份 ZIP 包含链接.");
            string relative = entry.FullName.Replace('\\', '/');
            bool directory = relative.EndsWith('/');
            string[] parts = (directory ? relative[..^1] : relative).Split('/');
            if (parts.Length > 128) throw new InvalidDataException("备份 ZIP 路径层级过深.");
            string target = destination;
            foreach (string part in parts)
            {
                try
                {
                    ValidateName(part);
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException("备份 ZIP 路径无效: " + relative, error);
                }

                target = Path.Combine(target, part);
            }

            if (directory)
            {
                if (entry.Length != 0) throw new InvalidDataException("备份 ZIP 的目录条目包含文件内容.");
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var input = entry.Open();
                uint crc = uint.MaxValue;
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    if (read > entry.Length - output.Length) throw new InvalidDataException("备份 ZIP 条目超过其声明大小.");
                    for (int i = 0; i < read; i++) crc = (crc >> 8) ^ CrcTable[(crc ^ buffer[i]) & 255];
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }

                if (output.Length != entry.Length || ~crc != entry.Crc32) throw new InvalidDataException("备份 ZIP 文件校验失败: " + relative);
            }
        }
    }

    private static uint[] CreateCrcTable()
    {
        // ZIP 使用 CRC-32/ISO-HDLC; 当前运行时的 ZIP 读取流不会验证校验值.
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xEDB88320u);
            table[i] = value;
        }

        return table;
    }

    private static IEnumerable<(string Path, bool Directory)> EnumerateTree(string root, CancellationToken token, bool skipLinks)
    {
        Stack<string> directories = new([root]);
        while (directories.TryPop(out string? directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    if (skipLinks) continue;
                    throw new IOException("世界或备份包含链接: " + path);
                }

                bool isDirectory = (attrs & FileAttributes.Directory) != 0;
                if (isDirectory) directories.Push(path);
                yield return (path, isDirectory);
            }
        }
    }

    private static void ValidateTree(string path, CancellationToken token)
    {
        foreach (var _ in EnumerateTree(path, token, skipLinks: false))
        {
        }
    }

    private static FileStream? CheckWorldStopped(string world)
    {
        string path = Path.Combine(world, "session.lock");
        EnsureNoLinks(path);
        if (!File.Exists(path)) return null;
        FileStream? stream = null;
        try
        {
            stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) stream.Lock(0, long.MaxValue);
            return stream;
        }
        catch (IOException error)
        {
            stream?.Dispose();
            throw new IOException("世界的会话锁不可用, 请先退出游戏.", error);
        }
    }

    private static (string World, string Backups) Resolve(string savesDirectory, string backupsDirectory, string worldDirectoryName)
    {
        string saves = Full(savesDirectory), backups = Full(backupsDirectory);
        if (IsWithin(saves, backups) || IsWithin(backups, saves)) throw new ArgumentException("存档目录与备份目录不能重叠.");
        string world = WorldPath(saves, worldDirectoryName);
        string worldBackups = WorldPath(backups, worldDirectoryName);
        EnsureNoLinks(world);
        EnsureNoLinks(worldBackups);
        if (!Directory.Exists(world) || !LooksLikeWorld(world)) throw new DirectoryNotFoundException("找不到指定的世界: " + world);
        return (world, worldBackups);
    }

    private static bool IsWithin(string parent, string path) => string.Equals(parent, path, PathComparison) || path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, PathComparison);

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string Temporary(string root) => Path.Combine(root, WorkingPrefix + Guid.NewGuid().ToString("N"));

    private static bool LooksLikeWorld(string path) => File.Exists(Path.Combine(path, "level.dat")) || File.Exists(Path.Combine(path, "level.dat_old")) || Directory.Exists(Path.Combine(path, "region"));

    private static MinecraftWorldBackup ToBackup(string path) => new(Path.GetFileName(path), File.GetLastWriteTimeUtc(path), new FileInfo(path).Length);

    private static string ExistingBackup(string directory, string backupFileName)
    {
        ValidateName(backupFileName);
        if (!string.Equals(Path.GetExtension(backupFileName), ".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("备份必须为 ZIP 文件.", nameof(backupFileName));
        string path = Path.Combine(directory, backupFileName);
        EnsureNoLinks(path);
        if (!File.Exists(path)) throw new FileNotFoundException("找不到指定的世界备份.", path);
        return path;
    }

    private static string WorldPath(string root, string name)
    {
        ValidateName(name);
        if (name.StartsWith(WorkingPrefix, PathComparison)) throw new ArgumentException("不能操作临时目录.", nameof(name));
        return Path.Combine(root, name);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || OperatingSystem.IsWindows() && (name.EndsWith('.') || name.EndsWith(' ') || IsWindowsDevice(name)))
            throw new ArgumentException("无效的存档或备份路径名称: " + name, nameof(name));
    }

    private static bool IsWindowsDevice(string name)
    {
        string stem = name.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "0123456789¹²³".Contains(stem[3], StringComparison.Ordinal);
    }

    internal static void EnsureNoLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if (IsLink(current))
                throw new IOException("不能访问链接文件或目录: " + current);
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void RemoveDetached(string path)
    {
        try
        {
            EnsureNoLinks(path);
            foreach (var item in EnumerateTree(path, CancellationToken.None, skipLinks: false))
            {
                var attributes = File.GetAttributes(item.Path);
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(item.Path, attributes & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("无法清理已移出的目录: " + path, error);
        }
    }
}
