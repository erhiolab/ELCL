using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using ErhioLab.Craft.Launcher.Core.Downloads;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>从 Mojang 版本 JSON 和资源索引生成标准 .minecraft 目录中的下载请求.</summary>
public static class MinecraftVanillaDownloadPlanner
{
    private static readonly Uri MirrorRoot = new("https://bmclapi2.bangbang93.com/");

    public static MinecraftVanillaDownloadPlan ParseVersion(
        string json, string expectedVersionId, string gameDirectory, string installedName, MinecraftManifestSource preferredSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ValidateVersionName(installedName);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || ReadString(root, "id") != expectedVersionId) throw new InvalidDataException("版本 JSON 与所选 Minecraft 版本不一致.");

        string gameRoot = Path.GetFullPath(gameDirectory);
        string versionFolder = SafeChild(gameRoot, $"versions/{installedName}");
        JsonElement downloads = Required(root, "downloads");
        DownloadFileRequest client = ParseDownload(Required(downloads, "client"), Path.Combine(versionFolder, installedName + ".jar"), preferredSource);
        JsonElement assetIndex = Required(root, "assetIndex");
        string indexId = ReadString(assetIndex, "id") ?? throw new InvalidDataException("版本 JSON 缺少资源索引 ID.");
        if (indexId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || indexId is "." or "..") throw new InvalidDataException("资源索引 ID 无效.");
        DownloadFileRequest index = ParseDownload(assetIndex, SafeChild(gameRoot, $"assets/indexes/{indexId}.json"), preferredSource);

        DownloadFileRequest? loggingConfiguration = null;
        if (root.TryGetProperty("logging", out JsonElement logging) && logging.ValueKind == JsonValueKind.Object &&
            logging.TryGetProperty("client", out JsonElement clientLogging) && clientLogging.ValueKind == JsonValueKind.Object &&
            clientLogging.TryGetProperty("file", out JsonElement logFile) && logFile.ValueKind == JsonValueKind.Object)
        {
            string logId = ReadString(logFile, "id") ?? throw new InvalidDataException("日志配置缺少文件名.");
            if (logId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || logId is "." or "..")
                throw new InvalidDataException("日志配置文件名无效.");
            loggingConfiguration = ParseDownload(logFile, SafeChild(gameRoot, $"assets/log_configs/{logId}"), preferredSource);
        }

        List<DownloadFileRequest> libraries = [];
        if (!root.TryGetProperty("libraries", out JsonElement libraryArray) || libraryArray.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("版本 JSON 缺少支持库列表.");
        if (libraryArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement library in libraryArray.EnumerateArray())
            {
                if (!AppliesToCurrentOs(library)) continue;
                if (!library.TryGetProperty("downloads", out JsonElement libraryDownloads))
                {
                    libraries.Add(LegacyLibrary(library, gameRoot, preferredSource));
                    if (library.TryGetProperty("natives", out JsonElement legacyNatives) && legacyNatives.ValueKind == JsonValueKind.Object &&
                        legacyNatives.TryGetProperty(CurrentOsName(), out JsonElement legacyNativeName) && legacyNativeName.ValueKind == JsonValueKind.String)
                    {
                        string legacyClassifier = legacyNativeName.GetString()!.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                        libraries.Add(LegacyLibrary(library, gameRoot, preferredSource, legacyClassifier));
                    }

                    continue;
                }

                if (libraryDownloads.TryGetProperty("artifact", out JsonElement artifact) && artifact.ValueKind == JsonValueKind.Object)
                    libraries.Add(ParseLibrary(artifact, gameRoot, preferredSource));

                if (!library.TryGetProperty("natives", out JsonElement natives) || natives.ValueKind != JsonValueKind.Object ||
                    !natives.TryGetProperty(CurrentOsName(), out JsonElement nativeName) || nativeName.ValueKind != JsonValueKind.String ||
                    !libraryDownloads.TryGetProperty("classifiers", out JsonElement classifiers)) continue;
                string classifier = nativeName.GetString()!.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                if (classifiers.TryGetProperty(classifier, out JsonElement nativeArtifact))
                    libraries.Add(ParseLibrary(nativeArtifact, gameRoot, preferredSource));
            }
        }

        return new MinecraftVanillaDownloadPlan(json, indexId, client, index,
            libraries.DistinctBy(file => file.DestinationPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            loggingConfiguration, ReadNonNegativeLong(assetIndex, "totalSize"));
    }

    public static IReadOnlyList<DownloadFileRequest> ParseAssets(string indexJson, string gameDirectory, MinecraftManifestSource preferredSource)
    {
        using JsonDocument document = JsonDocument.Parse(indexJson);
        JsonElement objects = Required(document.RootElement, "objects");
        string gameRoot = Path.GetFullPath(gameDirectory);
        List<DownloadFileRequest> files = [];
        foreach (JsonProperty item in objects.EnumerateObject())
        {
            string hash = ReadString(item.Value, "hash") ?? throw new InvalidDataException("资源对象缺少哈希值.");
            if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("资源对象的 SHA-1 无效.");
            string relative = hash[..2] + "/" + hash;
            long? size = ReadSize(item.Value);
            Uri official = new("https://resources.download.minecraft.net/" + relative);
            files.Add(new DownloadFileRequest(SafeChild(gameRoot, "assets/objects/" + relative),
                Sources(official, preferredSource), size, hash));
        }

        return files.DistinctBy(file => file.DestinationPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static DownloadFileRequest VersionMetadata(MinecraftVersion version, string destination, MinecraftManifestSource preferredSource) =>
        new(destination, Sources(version.MetadataUrl, preferredSource), null, version.Sha1);

    /// <summary>校验已存在的共享文件, 任务只提交缺失或损坏的文件.</summary>
    public static async Task<IReadOnlyList<DownloadFileRequest>> MissingFilesAsync(
        IEnumerable<DownloadFileRequest> files, CancellationToken cancellationToken = default)
    {
        List<DownloadFileRequest> missing = [];
        foreach (DownloadFileRequest file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file.DestinationPath))
            {
                missing.Add(file);
                continue;
            }

            FileInfo info = new(file.DestinationPath);
            if (file.ExpectedSize is { } size && info.Length != size)
            {
                missing.Add(file);
                continue;
            }

            if (file.ExpectedSha1 is { } expected)
            {
                await using FileStream input = File.OpenRead(file.DestinationPath);
                string actual = Convert.ToHexString(await SHA1.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) missing.Add(file);
            }
        }

        return missing;
    }

    public static async Task WriteInstalledVersionAsync(MinecraftVanillaDownloadPlan plan, string gameDirectory,
        string installedName, CancellationToken cancellationToken = default)
    {
        ValidateVersionName(installedName);
        string versionFolder = SafeChild(Path.GetFullPath(gameDirectory), $"versions/{installedName}");
        Directory.CreateDirectory(versionFolder);
        string destination = Path.Combine(versionFolder, installedName + ".json");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        JsonNode root = JsonNode.Parse(plan.VersionJson) ?? throw new InvalidDataException("版本 JSON 无效.");
        root["id"] = installedName;
        try
        {
            await File.WriteAllTextAsync(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static DownloadFileRequest ParseLibrary(JsonElement artifact, string gameRoot, MinecraftManifestSource source)
    {
        string path = ReadString(artifact, "path") ?? throw new InvalidDataException("支持库缺少存储路径.");
        return ParseDownload(artifact, SafeChild(gameRoot, "libraries/" + path), source);
    }

    private static DownloadFileRequest LegacyLibrary(JsonElement library, string gameRoot,
        MinecraftManifestSource source, string? classifier = null)
    {
        string coordinate = ReadString(library, "name") ?? throw new InvalidDataException("支持库缺少 Maven 坐标.");
        string[] parts = coordinate.Split(':');
        if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("支持库 Maven 坐标无效.");
        string version = parts[2];
        string extension = "jar";
        int extensionAt = version.IndexOf('@');
        if (extensionAt >= 0)
        {
            extension = version[(extensionAt + 1)..];
            version = version[..extensionAt];
        }

        string name = parts[1] + "-" + version;
        string? suffix = classifier ?? (parts.Length == 4 ? parts[3] : null);
        if (suffix is not null) name += "-" + suffix;
        string relative = parts[0].Replace('.', '/') + "/" + parts[1] + "/" + version + "/" + name + "." + extension;
        string baseUrl = ReadString(library, "url") ?? "https://libraries.minecraft.net/";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? root) || root.Scheme is not ("http" or "https"))
            throw new InvalidDataException("支持库下载地址无效.");
        return new DownloadFileRequest(SafeChild(gameRoot, "libraries/" + relative),
            Sources(new Uri(root.AbsoluteUri.TrimEnd('/') + "/" + relative), source));
    }

    private static DownloadFileRequest ParseDownload(JsonElement data, string destination, MinecraftManifestSource source)
    {
        string url = ReadString(data, "url") ?? throw new InvalidDataException("下载项缺少 URL.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("https" or "http"))
            throw new InvalidDataException("下载项 URL 无效.");
        return new DownloadFileRequest(destination, Sources(uri, source), ReadSize(data), ReadString(data, "sha1"));
    }

    private static IReadOnlyList<Uri> Sources(Uri official, MinecraftManifestSource preferred)
    {
        Uri? mirror = MirrorUrl(official);
        return mirror is null ? [official] : preferred == MinecraftManifestSource.Mirror ? [mirror, official] : [official, mirror];
    }

    internal static IReadOnlyList<Uri> GetSources(Uri official, MinecraftManifestSource preferred) => Sources(official, preferred);

    private static Uri? MirrorUrl(Uri official)
    {
        string host = official.Host.ToLowerInvariant();
        string prefix = host switch
        {
            "resources.download.minecraft.net" => "assets/",
            "libraries.minecraft.net" => "maven/",
            "piston-meta.mojang.com" or "piston-data.mojang.com" or "launchermeta.mojang.com" or "launcher.mojang.com" => "",
            _ => "\0"
        };
        return prefix == "\0" ? null : new Uri(MirrorRoot, prefix + official.PathAndQuery.TrimStart('/'));
    }

    private static bool AppliesToCurrentOs(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out JsonElement rules) || rules.ValueKind != JsonValueKind.Array) return true;
        bool allowed = false;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            if (rule.TryGetProperty("features", out _)) continue;
            if (rule.TryGetProperty("os", out JsonElement os) && os.ValueKind == JsonValueKind.Object)
            {
                string? name = ReadString(os, "name");
                if (name is not null && name != CurrentOsName()) continue;
                string? arch = ReadString(os, "arch");
                if (arch is not null && arch != (Environment.Is64BitOperatingSystem ? "x86_64" : "x86")) continue;
                string? version = ReadString(os, "version");
                if (version is not null && !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version)) continue;
            }

            allowed = ReadString(rule, "action") == "allow";
        }

        return allowed;
    }

    private static string CurrentOsName() => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    private static JsonElement Required(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : throw new InvalidDataException($"JSON 缺少 {name} 对象.");

    private static string? ReadString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? ReadSize(JsonElement data) => ReadNonNegativeLong(data, "size");

    private static long? ReadNonNegativeLong(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement size) && size.ValueKind == JsonValueKind.Number &&
        size.TryGetInt64(out long number) && number >= 0 ? number : null;

    private static string SafeChild(string root, string relative)
    {
        string destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("下载文件路径超出游戏目录.");
        return destination;
    }

    /// <summary>在创建任务或计算目标目录前验证安装名称; 不允许路径分隔符和目录跳转.</summary>
    public static void ValidateVersionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name is "." or ".." ||
            name.EndsWith('.') || name.EndsWith(' ') || name.Contains('/') || name.Contains('\\') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("版本名称无效.", nameof(name));
    }
}

public sealed record MinecraftVanillaDownloadPlan(
    string VersionJson,
    string AssetIndexId,
    DownloadFileRequest Client,
    DownloadFileRequest AssetIndex,
    IReadOnlyList<DownloadFileRequest> Libraries,
    DownloadFileRequest? LoggingConfiguration,
    long? AssetTotalSize
);
