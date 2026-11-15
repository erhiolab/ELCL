using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Mods;

/// <summary>读取本地模组及执行文件操作, 不依赖启动器配置.</summary>
public static class LocalModManager
{
    /// <summary>仅枚举模组文件路径, 不打开归档或计算指纹.</summary>
    public static IReadOnlyList<string> EnumerateFiles(string directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) return [];
        List<string> files = [];
        foreach (string path in Directory.EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsMod(path)) files.Add(path);
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static async Task<IReadOnlyList<LocalModInfo>> ScanAsync(string directory, CancellationToken cancellationToken = default)
    {
        List<LocalModInfo> result = [];
        foreach (string path in EnumerateFiles(directory, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                result.Add(await ReadAsync(path, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                result.Add(new LocalModInfo(path, Path.GetFileName(path), Path.GetFileName(path), null, "", [], [], !IsDisabled(path), "", 0, Error: error.Message));
            }
        }

        return result;
    }

    public static async Task<LocalModInfo> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new InvalidDataException("模组文件超过 512 MiB.");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        string sha1 = Convert.ToHexStringLower(await SHA1.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        input.Position = 0;
        uint fingerprint = Fingerprint(input, cancellationToken);
        input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 100000) throw new InvalidDataException("模组归档条目过多.");
        string name = Path.GetFileName(path), description = "", version = "";
        List<string> ids = [], loaders = [];
        byte[]? icon = null;
        string? iconPath = null;
        var fabric = zip.GetEntry("fabric.mod.json");
        var quilt = zip.GetEntry("quilt.mod.json");
        var toml = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml");
        var legacy = zip.GetEntry("mcmod.info");
        if (fabric is not null || quilt is not null)
        {
            using var doc = JsonDocument.Parse(ReadEntry(fabric ?? quilt!));
            var root = doc.RootElement;
            bool isFabric = fabric is not null;
            var data = isFabric ? root : root.GetProperty("quilt_loader");
            var metadata = !isFabric && data.TryGetProperty("metadata", out var meta) ? meta : data;
            name = Text(metadata, "name", name);
            description = Text(metadata, "description");
            version = Text(data, "version");
            if (Text(data, "id") is { Length: > 0 } id) ids.Add(id);
            loaders.Add(isFabric ? "fabric" : "quilt");
            if (metadata.TryGetProperty("icon", out var image))
            {
                if (image.ValueKind == JsonValueKind.String) iconPath = image.GetString();
                else if (image.ValueKind == JsonValueKind.Object)
                {
                    var value = image.EnumerateObject().Select(property => property.Value).FirstOrDefault(value => value.ValueKind == JsonValueKind.String);
                    if (value.ValueKind == JsonValueKind.String) iconPath = value.GetString();
                }
            }
        }
        else if (toml is not null)
        {
            string text = System.Text.Encoding.UTF8.GetString(ReadEntry(toml));
            name = Toml(text, "displayName", name);
            description = Toml(text, "description");
            version = Toml(text, "version");
            ids.AddRange(Regex.Matches(text, "(?m)^\\s*modId\\s*=\\s*[\"']([^\"']+)[\"']").Select(match => match.Groups[1].Value).Distinct());
            loaders.Add(toml.FullName.Contains("neoforge", StringComparison.Ordinal) ? "neoforge" : "forge");
            iconPath = Toml(text, "iconFile", Toml(text, "logoFile"));
        }
        else if (legacy is not null)
        {
            using var doc = JsonDocument.Parse(ReadEntry(legacy));
            var root = doc.RootElement;
            var list = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("modList", out var mods) ? mods : default;
            if (list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
            {
                var first = list[0];
                name = Text(first, "name", name);
                version = Text(first, "version");
                description = Text(first, "description");
                ids.AddRange(list.EnumerateArray().Select(mod => Text(mod, "modid")).Where(id => id.Length > 0));
                iconPath = Text(first, "logoFile");
            }

            loaders.Add("forge");
        }
        else if (zip.GetEntry("litemod.json") is { } lite)
        {
            using var doc = JsonDocument.Parse(ReadEntry(lite));
            name = Text(doc.RootElement, "displayName", Text(doc.RootElement, "name", name));
            version = Text(doc.RootElement, "version");
            loaders.Add("liteloader");
        }

        if (zip.GetEntry("META-INF/MANIFEST.MF") is { } manifest)
        {
            string content = System.Text.Encoding.UTF8.GetString(ReadEntry(manifest)).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            content = content.Split("\n\n", 2)[0];
            content = Regex.Replace(content, "\\n ([^\\n]*)", "$1");
            string Value(string key) => Regex.Match(content, "(?m)^" + Regex.Escape(key) + ": (.+)").Groups[1].Value.Trim();
            if (name == Path.GetFileName(path) && Value("Specification-Title") is { Length: > 0 } title) name = title;
            if (version.Length == 0 || version.Contains("${", StringComparison.Ordinal))
                version = Value("Implementation-Version") is { Length: > 0 } implementation ? implementation : Value("Specification-Version");
        }

        if (iconPath is { Length: > 0 } && zip.GetEntry(iconPath.TrimStart('/')) is { Length: <= 2097152 } entry) icon = ReadEntry(entry);
        return new LocalModInfo(path, Path.GetFileName(path), name, version, description, ids, loaders, !IsDisabled(path), sha1, fingerprint, icon);
    }

    /// <summary>先检查所有目标, 再复制到临时目录; 冲突时不覆盖已有文件.</summary>
    public static async Task<IReadOnlyList<string>> InstallAsync(string directory, IReadOnlyList<string> sourceFiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        var files = sourceFiles.Select(source => (Source: Path.GetFullPath(source), Target: Child(root, Path.GetFileName(source)))).ToArray();
        if (files.Select(file => IsDisabled(file.Target) ? Counterpart(file.Target) : file.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length) throw new IOException("选择的文件名称重复.");
        foreach (var file in files)
        {
            if (!IsMod(file.Source) || !File.Exists(file.Source)) throw new InvalidDataException("请选择有效的模组文件.");
            string enabled = IsDisabled(file.Target) ? Counterpart(file.Target) : file.Target;
            if (File.Exists(enabled) || File.Exists(enabled + ".disabled") || File.Exists(enabled + ".disable")) throw new IOException("已存在同名模组: " + Path.GetFileName(file.Target));
        }

        string staging = Child(root, ".elcl-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        List<string> published = [];
        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var input = File.OpenRead(files[i].Source);
                await using var output = File.Create(Path.Combine(staging, i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < files.Length; i++)
            {
                File.Move(Path.Combine(staging, i.ToString(System.Globalization.CultureInfo.InvariantCulture)), files[i].Target);
                published.Add(files[i].Target);
            }

            return published;
        }
        catch
        {
            foreach (string target in published) File.Delete(target);
            throw;
        }
        finally
        {
            Directory.Delete(staging, true);
        }
    }

    public static string SetEnabled(string directory, string path, bool enabled)
    {
        string current = ValidateMember(directory, path);
        if (!File.Exists(current)) throw new FileNotFoundException("模组文件已不存在.", current);
        if (enabled == !IsDisabled(current)) return current;
        string target = Counterpart(current);
        if (!enabled && File.Exists(current + ".disable")) throw new IOException("已存在同名禁用模组.");
        File.Move(current, target);
        return target;
    }

    public static void Delete(string directory, string path) => File.Delete(ValidateMember(directory, path));

    private static string ValidateMember(string directory, string path)
    {
        string root = Path.GetFullPath(directory), full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !IsMod(full))
            throw new ArgumentException("模组必须位于指定文件夹内.", nameof(path));
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("不在链接目录中修改模组.");
        return full;
    }

    private static string Child(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)) throw new ArgumentException("文件名无效.", nameof(name));
        string path = Path.Combine(root, name);
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("不在链接目录中安装模组.");
        return path;
    }

    private static bool IsDisabled(string path) => path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".disable", StringComparison.OrdinalIgnoreCase);
    private static string Counterpart(string path) => IsDisabled(path) ? path[..path.LastIndexOf('.')] : path + ".disabled";

    private static bool IsMod(string path)
    {
        string original = IsDisabled(path) ? Counterpart(path) : path;
        return original.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || original.EndsWith(".litemod", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > 2 * 1024 * 1024) throw new InvalidDataException("模组元数据超过 2 MiB.");
        using var stream = entry.Open();
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (output.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("模组元数据超过 2 MiB.");
            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }

    private static string Text(JsonElement root, string key, string fallback = "") => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private static string Toml(string text, string key, string fallback = "")
    {
        var match = Regex.Match(text, "(?m)^\\s*" + key + "\\s*=\\s*[\"']([^\"'\\r\\n]*)[\"']");
        return match.Success ? match.Groups[1].Value : fallback;
    }

    private static uint Fingerprint(Stream input, CancellationToken token)
    {
        byte[] buffer = new byte[65536];
        uint length = 0;
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < read; i++)
                if (buffer[i] is not (9 or 10 or 13 or 32))
                    length++;
        }

        input.Position = 0;
        const uint multiplier = 0x5bd1e995;
        uint hash = 1 ^ length, word = 0;
        int bytes = 0;
        unchecked
        {
            while ((read = input.Read(buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                for (int i = 0; i < read; i++)
                {
                    byte value = buffer[i];
                    if (value is 9 or 10 or 13 or 32) continue;
                    word |= (uint)value << (8 * bytes++);
                    if (bytes != 4) continue;
                    word *= multiplier;
                    word ^= word >> 24;
                    word *= multiplier;
                    hash = hash * multiplier ^ word;
                    bytes = 0;
                    word = 0;
                }
            }

            if (bytes > 0)
            {
                hash ^= word;
                hash *= multiplier;
            }

            hash ^= hash >> 13;
            hash *= multiplier;
            return hash ^ hash >> 15;
        }
    }
}
