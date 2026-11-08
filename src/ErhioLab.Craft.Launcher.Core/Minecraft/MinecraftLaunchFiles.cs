using System.IO.Compression;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Downloads;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public sealed record MinecraftNativeArchive(string Path, IReadOnlyList<string> Excludes);

/// <summary>启动所需文件/类路径与待解压的本地库. 无下载地址的条目只能由安装器提供.</summary>
public sealed record MinecraftLaunchFilePlan(
    IReadOnlyList<DownloadFileRequest> Files,
    IReadOnlyList<string> ClassPath,
    IReadOnlyList<MinecraftNativeArchive> Natives,
    DownloadFileRequest AssetIndex,
    string AssetIndexId,
    string? LoggingArgument,
    string? LoggingPath);

public static class MinecraftLaunchFiles
{
    public static MinecraftLaunchFilePlan Plan(MinecraftLaunchVersion version, MinecraftManifestSource source,
        MinecraftLaunchEnvironment? environment = null)
    {
        environment ??= MinecraftLaunchEnvironment.Current;
        using JsonDocument document = JsonDocument.Parse(version.Json);
        var root = document.RootElement;
        List<DownloadFileRequest> files = [];
        List<string> classPath = [];
        List<MinecraftNativeArchive> natives = [];

        DownloadFileRequest File(JsonElement data, string path) => new(path,
            data.TryGetProperty("url", out var url) && !string.IsNullOrWhiteSpace(url.GetString())
                ? MinecraftVanillaDownloadPlanner.GetSources(new Uri(url.GetString()!, UriKind.Absolute), source)
                : [],
            data.TryGetProperty("size", out var size) ? size.GetInt64() : null,
            data.TryGetProperty("sha1", out var hash) ? hash.GetString() : null);

        if (!version.HasCustomClientJar && root.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("client", out var client))
            files.Add(File(client, version.ClientJarPath));
        else files.Add(new(version.ClientJarPath, []));
        if (root.TryGetProperty("libraries", out var libraries))
        {
            foreach (var library in libraries.EnumerateArray())
            {
                if (!MinecraftLaunchRules.Allows(library, environment)) continue;
                bool hasDownloads = library.TryGetProperty("downloads", out var libraryDownloads);
                bool nativeOnly = library.TryGetProperty("natives", out var nativeNames);
                if (hasDownloads && libraryDownloads.TryGetProperty("artifact", out var artifact))
                {
                    string relative = artifact.TryGetProperty("path", out var path) ? path.GetString()! : MavenPath(library.GetProperty("name").GetString()!);
                    var file = File(artifact, MinecraftLaunchVersion.SafePath(version.GameDirectory, "libraries/" + relative));
                    files.Add(file);
                    classPath.Add(file.DestinationPath);
                }
                else if (!hasDownloads || !nativeOnly)
                {
                    var file = LegacyLibrary(library, version.GameDirectory, source);
                    files.Add(file);
                    classPath.Add(file.DestinationPath);
                }

                if (!nativeOnly || !nativeNames.TryGetProperty(environment.OsName, out var classifierValue)) continue;
                string classifier = classifierValue.GetString()!.Replace("${arch}", environment.NativeBits);
                DownloadFileRequest nativeFile;
                if (hasDownloads && libraryDownloads.TryGetProperty("classifiers", out var classifiers) && classifiers.TryGetProperty(classifier, out var nativeArtifact))
                {
                    string relative = nativeArtifact.GetProperty("path").GetString()!;
                    nativeFile = File(nativeArtifact, MinecraftLaunchVersion.SafePath(version.GameDirectory, "libraries/" + relative));
                }
                else nativeFile = LegacyLibrary(library, version.GameDirectory, source, classifier);

                files.Add(nativeFile);
                string[] excludes = library.TryGetProperty("extract", out var extract) && extract.TryGetProperty("exclude", out var excluded)
                    ? excluded.EnumerateArray().Select(value => value.GetString()!).ToArray()
                    : ["META-INF/"];
                natives.Add(new(nativeFile.DestinationPath, excludes));
            }
        }

        classPath.Add(version.ClientJarPath);
        JsonElement index = root.GetProperty("assetIndex");
        string indexId = MinecraftLaunchVersion.SafeName(index.GetProperty("id").GetString()!);
        var indexFile = File(index, MinecraftLaunchVersion.SafePath(version.GameDirectory, "assets/indexes/" + indexId + ".json"));
        files.Add(indexFile);
        string? logArgument = null, logPath = null;
        if (root.TryGetProperty("logging", out var logging) && logging.TryGetProperty("client", out var log))
        {
            var data = log.GetProperty("file");
            logPath = MinecraftLaunchVersion.SafePath(version.GameDirectory, "assets/log_configs/" + MinecraftLaunchVersion.SafeName(data.GetProperty("id").GetString()!));
            files.Add(File(data, logPath));
            logArgument = log.GetProperty("argument").GetString();
        }

        return new(files.DistinctBy(file => file.DestinationPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            classPath.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), natives, indexFile, indexId, logArgument, logPath);
    }

    /// <summary>提取至调用方为本次运行创建的目录, 阻止 ZIP 路径越界.</summary>
    public static async Task ExtractNativesAsync(MinecraftLaunchFilePlan plan, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var native in plan.Natives)
        {
            using var archive = ZipFile.OpenRead(native.Path);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/') || name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) || native.Excludes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))) continue;
                string target = MinecraftLaunchVersion.SafePath(directory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>为旧版游戏建立 assets/virtual 或 resources 映射; 对象文件应先校验/补全.</summary>
    public static async Task MaterializeLegacyAssetsAsync(string indexJson, string gameDirectory, string workingDirectory,
        string indexId, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(indexJson);
        var root = document.RootElement;
        List<string> destinations = [];
        if (root.TryGetProperty("virtual", out var virtualFlag) && virtualFlag.GetBoolean())
            destinations.Add(MinecraftLaunchVersion.SafePath(gameDirectory, "assets/virtual/" + MinecraftLaunchVersion.SafeName(indexId)));
        if (root.TryGetProperty("map_to_resources", out var resources) && resources.GetBoolean()) destinations.Add(Path.Combine(workingDirectory, "resources"));
        foreach (string destination in destinations)
        {
            foreach (var item in root.GetProperty("objects").EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string hash = item.Value.GetProperty("hash").GetString()!;
                if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("资源哈希无效.");
                string source = MinecraftLaunchVersion.SafePath(gameDirectory, "assets/objects/" + hash[..2] + "/" + hash);
                string target = MinecraftLaunchVersion.SafePath(destination, item.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var input = File.OpenRead(source))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, target, true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    private static DownloadFileRequest LegacyLibrary(JsonElement library, string game, MinecraftManifestSource source, string? classifier = null)
    {
        string path = MavenPath(library.GetProperty("name").GetString()!, classifier);
        string root = library.TryGetProperty("url", out var url) ? url.GetString()! : "https://libraries.minecraft.net/";
        return new(MinecraftLaunchVersion.SafePath(game, "libraries/" + path),
            MinecraftVanillaDownloadPlanner.GetSources(new Uri(root.TrimEnd('/') + "/" + path), source));
    }

    private static string MavenPath(string name, string? classifier = null)
    {
        string[] extension = name.Split('@');
        string[] parts = extension[0].Split(':');
        if (parts.Length is < 3 or > 4) throw new InvalidDataException("支持库 Maven 坐标无效.");
        string suffix = classifier ?? (parts.Length == 4 ? parts[3] : "");
        return parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/" + parts[1] + "-" + parts[2] + (suffix.Length > 0 ? "-" + suffix : "") + "." + (extension.Length > 1 ? extension[1] : "jar");
    }
}
