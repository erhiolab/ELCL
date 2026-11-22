using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>读取本地资源包和光影包的元数据, 不把包解压到磁盘, 也不执行或下载包内容.</summary>
public static class LocalMinecraftPackScanner
{
    private const int MetadataLimit = 128 * 1024;
    private const int IconLimit = 4 * 1024 * 1024;
    private const int EntryLimit = 100_000;
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 32 };

    public static IReadOnlyList<string> Enumerate(string directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) return [];
        RejectLink(root);
        List<string> paths = [];
        foreach (string item in Directory.EnumerateFileSystemEntries(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(item) || IsZip(item)) paths.Add(item);
        }

        return paths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static Task<LocalMinecraftPack> ReadAsync(string path, MinecraftPackKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        string fullPath = Path.GetFullPath(path);
        return Task.Run(() => ReadCore(fullPath, kind, cancellationToken), cancellationToken);
    }

    public static Task<MinecraftPackFormat?> ReadClientResourceFormatAsync(string jarPath, CancellationToken cancellationToken = default)
    {
        string path = Path.GetFullPath(jarPath);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return null;
            RejectLink(path);
            using var zip = ZipFile.OpenRead(path);
            byte[]? bytes = ReadEntry(zip.GetEntry("version.json"), MetadataLimit, cancellationToken);
            if (bytes is null) return null;
            using var json = JsonDocument.Parse(bytes, JsonOptions);
            JsonElement versions = Property(json.RootElement, "pack_version");
            return ParseFormat(Property(versions, "resource_major")) is { } major
                ? new MinecraftPackFormat(major.Major, ParseFormat(Property(versions, "resource_minor"))?.Major)
                : ParseFormat(Property(versions, "resource"));
        }, cancellationToken);
    }

    public static async Task<int> ImportAsync(string directory, IEnumerable<string> sources, MinecraftPackKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var paths = sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return 0;
        if (paths.Length > 4096) throw new InvalidDataException("一次最多导入 4096 个压缩包.");
        if (Path.GetDirectoryName(root) is { } parent && Directory.Exists(parent)) RejectLink(parent);
        if (Directory.Exists(root)) RejectLink(root);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        var plan = new List<(string Source, string Destination)>();
        foreach (string source in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(source);
            if (!IsZip(source) || !File.Exists(source) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("只能导入 ZIP 格式的包: " + name);
            string destination = Path.Combine(root, name);
            if (!names.Add(name) || File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("存在同名文件, 不会覆盖: " + name);
            var pack = await ReadAsync(source, kind, cancellationToken).ConfigureAwait(false);
            if (pack.Error is { } error) throw new InvalidDataException(name + ": " + error);
            plan.Add((source, destination));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        RejectLink(root);
        List<(string Temporary, string Destination)> staged = [];
        List<string> installed = [];
        try
        {
            foreach (var (source, destination) in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectLink(source);
                string temporary = Path.Combine(root, ".elcl-pack-" + Guid.NewGuid().ToString("N") + ".part");
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                staged.Add((temporary, destination));
                await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            foreach (var (temporary, destination) in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination);
                installed.Add(destination);
            }

            return installed.Count;
        }
        catch
        {
            foreach (string path in installed) File.Delete(path);
            throw;
        }
        finally
        {
            foreach (var item in staged) File.Delete(item.Temporary);
        }
    }

    private static LocalMinecraftPack ReadCore(string path, MinecraftPackKind kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool directory = Directory.Exists(path);
        string name = directory ? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) : Path.GetFileNameWithoutExtension(path);
        LocalMinecraftPack pack = new(path, name, directory);
        try
        {
            RejectLink(path);
            if (directory)
            {
                if (kind == MinecraftPackKind.ShaderPack)
                {
                    string shaders = Path.Combine(path, "shaders");
                    if (!Directory.Exists(shaders)) throw new InvalidDataException("找不到 shaders 文件夹.");
                    RejectLink(shaders);
                }

                return ReadMetadata(pack, kind, (entry, limit) => ReadFile(path, entry, limit, token), "shaders/");
            }

            pack = pack with { Size = new FileInfo(path).Length };
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > EntryLimit) throw new InvalidDataException("ZIP 条目过多.");
            string shaderRoot = kind == MinecraftPackKind.ShaderPack ? FindShaderRoot(zip, token) : "shaders/";
            string prefix = shaderRoot[..^"shaders/".Length];
            return ReadMetadata(pack, kind, (entry, limit) => ReadEntry(zip.GetEntry(entry), limit, token), shaderRoot, prefix);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException)
        {
            return pack with { Error = error.Message };
        }
    }

    private static LocalMinecraftPack ReadMetadata(LocalMinecraftPack pack, MinecraftPackKind kind, Func<string, int, byte[]?> read,
        string shaderRoot, string prefix = "")
    {
        bool resource = kind == MinecraftPackKind.ResourcePack;
        byte[]? metadata = read(prefix + "pack.mcmeta", MetadataLimit) ?? (prefix.Length > 0 ? read("pack.mcmeta", MetadataLimit) : null);
        ReadOnlyMemory<byte> jsonBytes = metadata;
        if (jsonBytes.Span.StartsWith(Encoding.UTF8.Preamble)) jsonBytes = jsonBytes[Encoding.UTF8.Preamble.Length..];
        using var json = metadata is null ? null : JsonDocument.Parse(jsonBytes, JsonOptions);
        JsonElement packInfo = json is null ? default : Property(json.RootElement, "pack");
        if (resource && packInfo.ValueKind != JsonValueKind.Object) throw new InvalidDataException("找不到有效的 pack.mcmeta.");
        var properties = !resource && read(shaderRoot + "shaders.properties", MetadataLimit) is { } bytes ? ReadProperties(bytes) : null;

        string? Get(string key) => Trim(MinecraftPackText.String(packInfo, key) ?? (json is null ? null : MinecraftPackText.String(json.RootElement, key)) ?? properties?.GetValueOrDefault(key));

        var description = MinecraftPackText.Parse(Property(packInfo, "description"));
        if (description.Count == 0 && json is not null) description = MinecraftPackText.Parse(Property(json.RootElement, "description"));
        if (description.Count == 0 && Get("description") is { } plain) description = MinecraftPackText.Parse(plain);
        var (minimum, maximum) = resource ? FormatRange(packInfo) : (null, null);
        if (resource && minimum is null && maximum is null) throw new InvalidDataException("pack.mcmeta 未声明资源包格式.");
        if (minimum is { } low && maximum is { } high && (low.Major, low.Minor ?? 0).CompareTo((high.Major, high.Minor ?? int.MaxValue)) > 0)
            throw new InvalidDataException("资源包格式范围无效.");
        return pack with
        {
            Name = Get("name") ?? pack.Name,
            Author = Get("author"), Version = Get("version"),
            MinecraftVersions = Get("minecraft_version") ?? Get("minecraftVersion") ?? Get("minecraft_versions")
            ?? VersionList(packInfo) ?? (json is null ? null : VersionList(json.RootElement)),
            MinimumFormat = minimum, MaximumFormat = maximum, Description = description,
            Icon = ReadIcon(read, prefix)
        };
    }

    private static string? VersionList(JsonElement root)
    {
        JsonElement versions = Property(root, "minecraft_versions");
        if (versions.ValueKind != JsonValueKind.Array) return null;
        return Trim(string.Join(", ", versions.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Take(32)));
    }

    private static byte[]? ReadIcon(Func<string, int, byte[]?> read, string prefix)
    {
        try
        {
            return read(prefix + "pack.png", IconLimit) ?? (prefix.Length > 0 ? read("pack.png", IconLimit) : null);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (MinecraftPackFormat? Minimum, MinecraftPackFormat? Maximum) FormatRange(JsonElement pack)
    {
        var minimum = ParseFormat(Property(pack, "min_format"));
        var maximum = ParseFormat(Property(pack, "max_format"));
        if (minimum is not null || maximum is not null) return (minimum, maximum);
        JsonElement supported = Property(pack, "supported_formats");
        if (supported.ValueKind == JsonValueKind.Array && supported.GetArrayLength() == 2)
            return (ParseFormat(supported[0]), ParseFormat(supported[1]));
        if (supported.ValueKind == JsonValueKind.Object)
            return (ParseFormat(Property(supported, "min_inclusive")), ParseFormat(Property(supported, "max_inclusive")));
        var format = ParseFormat(supported) ?? ParseFormat(Property(pack, "pack_format"));
        return (format, format);
    }

    private static MinecraftPackFormat? ParseFormat(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= 0) return new(number);
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is 1 or 2 && ParseFormat(value[0]) is { } major)
            return value.GetArrayLength() == 1 ? major : ParseFormat(value[1]) is { } minor ? new(major.Major, minor.Major) : null;
        if (value.ValueKind == JsonValueKind.Object && ParseFormat(Property(value, "major")) is { } first)
            return new(first.Major, ParseFormat(Property(value, "minor"))?.Major);
        return null;
    }

    private static string FindShaderRoot(ZipArchive zip, CancellationToken token)
    {
        string? nested = null;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string name = entry.FullName;
            if (name.EndsWith('/')) continue;
            if (name.StartsWith("shaders/", StringComparison.Ordinal)) return "shaders/";
            int slash = name.IndexOf('/');
            if (slash <= 0 || !name.AsSpan(slash + 1).StartsWith("shaders/", StringComparison.Ordinal)) continue;
            string folder = name[..slash];
            if (folder is "." or ".." || folder.Contains('\\') || folder.StartsWith("__MACOSX", StringComparison.Ordinal)) continue;
            nested ??= folder + "/shaders/";
        }

        return nested ?? throw new InvalidDataException("找不到 shaders 文件夹.");
    }

    private static Dictionary<string, string> ReadProperties(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes).TrimStart('\ufeff');
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or '!') continue;
            int separator = trimmed.IndexOfAny(['=', ':']);
            if (separator > 0) values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return values;
    }

    private static byte[]? ReadFile(string root, string entry, int limit, CancellationToken token)
    {
        string path = Path.Combine(root, entry);
        if (!File.Exists(path)) return null;
        RejectLink(path);
        using var stream = File.OpenRead(path);
        return ReadBounded(stream, stream.Length, limit, token);
    }

    private static byte[]? ReadEntry(ZipArchiveEntry? entry, int limit, CancellationToken token)
    {
        if (entry is null) return null;
        using var input = entry.Open();
        return ReadBounded(input, entry.Length, limit, token);
    }

    private static byte[] ReadBounded(Stream input, long length, int limit, CancellationToken token)
    {
        if (length < 0 || length > limit) throw new InvalidDataException("包内的元数据或图标超过大小限制.");
        token.ThrowIfCancellationRequested();
        byte[] bytes = new byte[(int)length];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("包内文件大小与记录不一致.");
        return bytes;
    }

    private static JsonElement Property(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var property) ? property : default;
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 256)];
    private static bool IsZip(string path) => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不读取链接目录或文件: " + path);
    }
}
