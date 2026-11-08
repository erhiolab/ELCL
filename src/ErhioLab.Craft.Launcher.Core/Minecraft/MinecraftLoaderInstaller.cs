using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using ErhioLab.Craft.Launcher.Core.Downloads;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>安装加载器; 网络/下载队列/工作目录和 Java 进程均由调用方提供.</summary>
public sealed partial class MinecraftLoaderInstaller(HttpClient httpClient)
{
    private const string Mirror = "https://bmclapi2.bangbang93.com/";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultVersionName(string gameVersion, IReadOnlyList<MinecraftLoaderSelection> selections) =>
        gameVersion + string.Concat(selections.OrderBy(s => s.Kind == MinecraftLoaderKind.OptiFine ? 1 : 0)
            .Select(s => "-" + s.Kind + "_" + s.Version.Version.Replace(' ', '_')));

    public static void ValidateSelections(string gameVersion, IReadOnlyList<MinecraftLoaderSelection> selections)
    {
        MinecraftVanillaDownloadPlanner.ValidateVersionName(gameVersion);
        foreach (var selection in selections)
        {
            if (!Enum.IsDefined(selection.Kind)) throw new ArgumentException("未知加载器类型.", nameof(selections));
            MinecraftVanillaDownloadPlanner.ValidateVersionName(selection.Version.Version.Replace(' ', '_'));
        }

        for (int first = 0; first < selections.Count; first++)
        for (int second = first + 1; second < selections.Count; second++)
            if (!MinecraftLoaderCompatibility.CanCombine(gameVersion, selections[first].Kind, selections[first].Version, selections[second].Kind, selections[second].Version))
                throw new ArgumentException("所选加载器不能共同安装.", nameof(selections));
    }

    public async Task<MinecraftLoaderInstallPlan> PrepareAsync(string gameVersion, string installedName,
        IReadOnlyList<MinecraftLoaderSelection> selections, string gameDirectory, string workDirectory,
        MinecraftManifestSource preferredSource, Func<IReadOnlyList<DownloadFileRequest>, CancellationToken, Task> download,
        Action<string>? log = null, CancellationToken cancellationToken = default,
        string? baseVersionJsonPath = null, string? baseClientJarPath = null)
    {
        ValidateSelections(gameVersion, selections);
        MinecraftVanillaDownloadPlanner.ValidateVersionName(installedName);
        if (selections.Count == 0) throw new ArgumentException("未选择需要安装的加载器.", nameof(selections));
        var plan = new MinecraftLoaderInstallPlan
        {
            GameDirectory = Path.GetFullPath(gameDirectory), WorkDirectory = Path.GetFullPath(workDirectory),
            GameVersion = gameVersion, InstalledName = installedName, Source = preferredSource, Download = download, Log = log,
            BaseVersionJsonPath = Path.GetFullPath(baseVersionJsonPath ?? Safe(gameDirectory, $"versions/{gameVersion}/{gameVersion}.json")),
            BaseClientJarPath = Path.GetFullPath(baseClientJarPath ?? Safe(gameDirectory, $"versions/{gameVersion}/{gameVersion}.jar")),
            VersionJson = new JsonObject { ["id"] = installedName, ["inheritsFrom"] = gameVersion, ["clientVersion"] = gameVersion, ["libraries"] = new JsonArray() }
        };
        var original = ParseObject(await File.ReadAllTextAsync(plan.BaseVersionJsonPath, cancellationToken).ConfigureAwait(false));
        if (original["id"]?.GetValue<string>() != gameVersion)
            throw new InvalidDataException("原版元数据与所选 Minecraft 版本不匹配.");
        if (!File.Exists(plan.BaseClientJarPath) || new FileInfo(plan.BaseClientJarPath).Length == 0)
            throw new FileNotFoundException("原版客户端 JAR 尚未准备完成.", plan.BaseClientJarPath);
        Directory.CreateDirectory(plan.WorkDirectory);
        foreach (var selection in selections.OrderBy(s => s.Kind == MinecraftLoaderKind.OptiFine ? 1 : 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            log?.Invoke($"准备 {selection.Kind} {selection.Version.Version}");
            switch (selection.Kind)
            {
                case MinecraftLoaderKind.Fabric:
                case MinecraftLoaderKind.Quilt:
                    string host = selection.Kind == MinecraftLoaderKind.Fabric ? "https://meta.fabricmc.net/v2/" : "https://meta.quiltmc.org/v3/";
                    string route = $"versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(selection.Version.Version)}/profile/json";
                    string mirrorRoute = selection.Kind == MinecraftLoaderKind.Fabric ? "fabric-meta/v2/" : "quilt-meta/v3/";
                    plan.VersionJson = ParseObject(await FetchAsync(host + route, Mirror + mirrorRoute + route, preferredSource, cancellationToken).ConfigureAwait(false));
                    AddLibraries(plan, plan.VersionJson["libraries"] as JsonArray);
                    break;
                case MinecraftLoaderKind.Forge:
                case MinecraftLoaderKind.NeoForge:
                    await PrepareForgeAsync(plan, selection, cancellationToken).ConfigureAwait(false);
                    break;
                case MinecraftLoaderKind.LiteLoader:
                    await PrepareLiteLoaderAsync(plan, selection, cancellationToken).ConfigureAwait(false);
                    break;
                case MinecraftLoaderKind.OptiFine:
                    await PrepareOptiFineAsync(plan, selection, selections.Count > 1, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        plan.VersionJson["id"] = installedName;
        plan.VersionJson["inheritsFrom"] = gameVersion;
        plan.VersionJson["clientVersion"] = gameVersion;
        plan.VersionJson["type"] = "release";
        if (string.IsNullOrWhiteSpace(plan.VersionJson["mainClass"]?.GetValue<string>())) throw new InvalidDataException("加载器配置缺少 mainClass.");
        return plan;
    }

    private async Task PrepareForgeAsync(MinecraftLoaderInstallPlan plan, MinecraftLoaderSelection selection, CancellationToken token)
    {
        bool neo = selection.Kind == MinecraftLoaderKind.NeoForge;
        bool legacyNeo = neo && plan.GameVersion == "1.20.1";
        string version = neo && !legacyNeo ? selection.Version.Version : plan.GameVersion + "-" + selection.Version.Version;
        string group = neo ? "net/neoforged/" : "net/minecraftforge/";
        string artifact = neo && !legacyNeo ? "neoforge" : "forge";
        string relative = group + artifact + "/" + version + "/" + artifact + "-" + version + "-installer.jar";
        string official = (neo ? "https://maven.neoforged.net/releases/" : "https://maven.minecraftforge.net/") + relative;
        string installer = Path.Combine(plan.WorkDirectory, selection.Kind + "-installer.jar");
        await plan.Download([new DownloadFileRequest(installer, Ordered(official, Mirror + "maven/" + relative, plan.Source))], token).ConfigureAwait(false);
        using var zip = ZipFile.OpenRead(installer);
        var metadata = ParseObject(ReadEntry(zip, "install_profile.json"));
        if (metadata["minecraft"]?.GetValue<string>() is { } mc && mc != plan.GameVersion) throw new InvalidDataException("安装器对应的 Minecraft 版本不匹配.");
        string extracted = Path.Combine(plan.WorkDirectory, selection.Kind.ToString());
        Directory.CreateDirectory(extracted);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Name.Length == 0) continue;
            if (entry.FullName.StartsWith("maven/", StringComparison.Ordinal))
                ExtractEntry(entry, Safe(Path.Combine(plan.GameDirectory, "libraries"), entry.FullName[6..]));
            else if (entry.FullName.StartsWith("data/", StringComparison.Ordinal)) ExtractEntry(entry, Safe(extracted, entry.FullName));
        }

        if (metadata["install"] is JsonObject install)
        {
            var coordinate = install["path"]!.GetValue<string>();
            var target = LibraryPath(plan, coordinate);
            ExtractEntry(zip.GetEntry(install["filePath"]!.GetValue<string>()) ?? throw new InvalidDataException("安装器缺少主文件."), target);
            plan.VersionJson = metadata["versionInfo"]?.DeepClone() as JsonObject ?? throw new InvalidDataException("安装器缺少版本配置.");
        }
        else
        {
            plan.VersionJson = ParseObject(ReadEntry(zip, metadata["json"]?.GetValue<string>().TrimStart('/') ?? "version.json"));
            plan.ForgeProfiles.Add((metadata, installer, extracted));
        }

        AddLibraries(plan, metadata["libraries"] as JsonArray);
        AddLibraries(plan, plan.VersionJson["libraries"] as JsonArray);
        // 官方处理器也需要 Mojang 映射, 提前通过调用方的下载队列取得.
        var baseJson = ParseObject(await File.ReadAllTextAsync(plan.BaseVersionJsonPath!, token).ConfigureAwait(false));
        if (baseJson["downloads"]?["client_mappings"] is JsonObject mapping && metadata["data"]?["MOJMAPS"]?["client"]?.GetValue<string>() is { } maps)
            plan.Dependencies.Add(Request(Resolve(plan, metadata, installer, extracted, maps), mapping, plan.Source));
    }

    private async Task PrepareLiteLoaderAsync(MinecraftLoaderInstallPlan plan, MinecraftLoaderSelection selection, CancellationToken token)
    {
        string text = await FetchAsync("https://dl.liteloader.com/versions/versions.json", Mirror + "liteloader/list?mcversion=" + Uri.EscapeDataString(plan.GameVersion), plan.Source, token).ConfigureAwait(false);
        var root = JsonNode.Parse(text) ?? throw new InvalidDataException("LiteLoader 元数据为空.");
        JsonObject? build = null;
        if (root is JsonObject document && document["versions"] is JsonObject all && all[plan.GameVersion] is JsonObject game)
        {
            foreach (string group in new[] { "artefacts", "snapshots" })
                if (game[group]?["com.mumfrey:liteloader"] is JsonObject builds)
                    build ??= builds.Select(entry => entry.Value).OfType<JsonObject>().FirstOrDefault(item => item["version"]?.GetValue<string>() == selection.Version.Version);
        }
        else
        {
            IEnumerable<JsonNode?> entries = root is JsonArray array ? array : new[] { root };
            build = entries.OfType<JsonObject>().FirstOrDefault(item => item["version"]?.GetValue<string>() == selection.Version.Version)?["build"] as JsonObject;
        }

        if (build is null) throw new InvalidDataException("未找到所选 LiteLoader 构建.");
        var libraries = build["libraries"]?.DeepClone() as JsonArray ?? new JsonArray();
        // LiteLoader 的 ASM 依赖未声明仓库, 且不一定存在于 Mojang 支持库站点.
        foreach (var library in libraries.OfType<JsonObject>())
            if (library["name"]?.GetValue<string>().StartsWith("org.ow2.asm:", StringComparison.Ordinal) == true
                && library["url"] is null && library["downloads"] is null)
                library["url"] = "https://repo.maven.apache.org/maven2/";
        string coordinate = "com.mumfrey:liteloader:" + selection.Version.Version;
        string relative = MavenPath(coordinate);
        string official = "https://dl.liteloader.com/versions/" + relative;
        try
        {
            official = await ResolveLiteLoaderArtifactAsync(new Uri(official), token).ConfigureAwait(false);
            plan.Log?.Invoke("LiteLoader 官方文件地址: " + official);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            // 地址解析失败不能阻止下载队列尝试镜像或重新访问官方来源.
            plan.Log?.Invoke("LiteLoader 官方文件地址解析失败, 将保留原地址和镜像回退: " + error);
        }

        libraries.Add(new JsonObject
        {
            ["name"] = coordinate,
            ["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject { ["path"] = relative, ["url"] = official }
            }
        });
        plan.VersionJson["libraries"] = libraries;
        plan.VersionJson["mainClass"] = "net.minecraft.launchwrapper.Launch";
        plan.VersionJson["arguments"] = new JsonObject { ["game"] = new JsonArray("--tweakClass", build["tweakClass"]?.GetValue<string>() ?? "com.mumfrey.liteloader.launch.LiteLoaderTweaker") };
        AddLibraries(plan, libraries);
    }

    private async Task<string> ResolveLiteLoaderArtifactAsync(Uri source, CancellationToken token)
    {
        // 旧站点会把 HTTPS 重定向到 HTTP, HttpClient 默认拒绝降级.
        // 逐跳使用 HTTPS, 取得仓库中实际的时间戳构建地址, 不修改通用下载器的策略.
        for (int redirects = 0; redirects < 10; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            Uri current = response.RequestMessage?.RequestUri ?? source;
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                Uri location = response.Headers.Location ?? throw new HttpRequestException("LiteLoader 重定向缺少目标地址.");
                source = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (source.Scheme == Uri.UriSchemeHttp)
                    source = new UriBuilder(source) { Scheme = Uri.UriSchemeHttps, Port = source.IsDefaultPort ? -1 : source.Port }.Uri;
                if (source.Scheme != Uri.UriSchemeHttps)
                    throw new HttpRequestException("LiteLoader 重定向目标必须使用 HTTPS.");
                continue;
            }

            response.EnsureSuccessStatusCode();
            return current.AbsoluteUri;
        }

        throw new HttpRequestException("LiteLoader 文件地址重定向次数过多.");
    }

    private async Task PrepareOptiFineAsync(MinecraftLoaderInstallPlan plan, MinecraftLoaderSelection selection, bool withForge, CancellationToken token)
    {
        string edition = selection.Version.Version.Replace(' ', '_');
        string fileName = (selection.Version.IsPreview ? "preview_" : "") + "OptiFine_" + plan.GameVersion + "_" + edition + ".jar";
        string downloadPage = "https://optifine.net/adloadx?f=" + Uri.EscapeDataString(fileName);
        string? official = null;
        try
        {
            string html = await httpClient.GetStringAsync(downloadPage, token).ConfigureAwait(false);
            var match = Regex.Match(html, @"downloadx\?f=[^""'<>\s]+", RegexOptions.None, TimeSpan.FromSeconds(2));
            if (match.Success) official = "https://optifine.net/" + WebUtility.HtmlDecode(match.Value);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            plan.Log?.Invoke("OptiFine 官方下载地址获取失败, 将使用回退来源: " + error.Message);
        }

        string[] parts = edition.Split('_', 3);
        if (parts.Length < 3) throw new InvalidDataException("OptiFine 版本标识无效.");
        string mirror = Mirror + "optifine/" + Uri.EscapeDataString(plan.GameVersion) + "/" + parts[0] + "_" + parts[1] + "/" + Uri.EscapeDataString(parts[2]);
        var sources = official is null ? new[] { new Uri(mirror) } : Ordered(official, mirror, plan.Source);
        string installer = Path.Combine(plan.WorkDirectory, fileName);
        await plan.Download([new DownloadFileRequest(installer, sources)], token).ConfigureAwait(false);
        using var zip = ZipFile.OpenRead(installer);
        string coordinate = "optifine:OptiFine:" + plan.GameVersion + "_" + edition;
        string patched = LibraryPath(plan, coordinate);
        Directory.CreateDirectory(Path.GetDirectoryName(patched)!);
        if (zip.GetEntry("optifine/Patcher.class") is not null)
            plan.Processes.Add((installer, "optifine.Patcher", [plan.BaseClientJarPath!, installer, patched]));
        else File.Copy(installer, patched, true);
        plan.GeneratedFiles.Add((patched, null));
        if (withForge)
        {
            string mods = Safe(plan.GameDirectory, $"versions/{plan.InstalledName}/mods/{fileName}");
            plan.Copies.Add((patched, mods));
            return;
        }

        var libraries = plan.VersionJson["libraries"]!.AsArray();
        libraries.Add(LocalLibrary(coordinate));
        var wrapper = zip.Entries.FirstOrDefault(entry => entry.Name.StartsWith("launchwrapper-of-", StringComparison.Ordinal) && entry.Name.EndsWith(".jar", StringComparison.Ordinal));
        if (wrapper is not null)
        {
            string wrapperVersion = wrapper.Name[17..^4];
            string wrapperCoordinate = "optifine:launchwrapper-of:" + wrapperVersion;
            ExtractEntry(wrapper, LibraryPath(plan, wrapperCoordinate));
            libraries.Add(LocalLibrary(wrapperCoordinate));
        }
        else
        {
            var library = new JsonObject { ["name"] = "net.minecraft:launchwrapper:1.12" };
            libraries.Add(library);
            AddLibraries(plan, new JsonArray(library.DeepClone()));
        }

        plan.VersionJson["mainClass"] = "net.minecraft.launchwrapper.Launch";
        plan.VersionJson["arguments"] = new JsonObject { ["game"] = new JsonArray("--tweakClass", "optifine.OptiFineTweaker") };
    }

    private async Task<string> FetchAsync(string official, string mirror, MinecraftManifestSource source, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var uri in Ordered(official, mirror, source).Distinct())
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    string json = await httpClient.GetStringAsync(uri, token).ConfigureAwait(false);
                    using var document = JsonDocument.Parse(json);
                    return json;
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException && !token.IsCancellationRequested)
                {
                    failures.Add(new HttpRequestException("安装元数据来源失败: " + uri, error));
                    if (error is HttpRequestException { StatusCode: HttpStatusCode.NotFound }) break;
                    if (attempt == 0) await Task.Delay(250, token).ConfigureAwait(false);
                }
            }
        }

        throw new HttpRequestException("无法获取加载器安装元数据.", new AggregateException(failures));
    }

    private static IReadOnlyList<Uri> Ordered(string official, string mirror, MinecraftManifestSource source) => source == MinecraftManifestSource.Mirror
        ? [new Uri(mirror), new Uri(official)]
        : [new Uri(official), new Uri(mirror)];

    private static JsonObject ParseObject(string json) => JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("安装元数据必须是 JSON 对象.");

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader((zip.GetEntry(name) ?? throw new InvalidDataException("安装器中缺少 " + name)).Open());
        return reader.ReadToEnd();
    }

    private static string Safe(string root, string relative) => MinecraftLaunchVersion.SafePath(root, relative);

    private static void ExtractEntry(ZipArchiveEntry entry, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        entry.ExtractToFile(path, true);
    }

    private static string MavenPath(string coordinate)
    {
        var extension = coordinate.Split('@');
        var parts = extension[0].Split(':');
        if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("无效 Maven 坐标.");
        return parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/" + parts[1] + "-" + parts[2]
               + (parts.Length == 4 ? "-" + parts[3] : "") + "." + (extension.Length > 1 ? extension[1] : "jar");
    }

    private static string LibraryPath(MinecraftLoaderInstallPlan plan, string coordinate) => Safe(Path.Combine(plan.GameDirectory, "libraries"), MavenPath(coordinate));

    private static JsonObject LocalLibrary(string coordinate) => new()
    {
        ["name"] = coordinate,
        ["downloads"] = new JsonObject { ["artifact"] = new JsonObject { ["path"] = MavenPath(coordinate), ["url"] = "" } }
    };

    private static DownloadFileRequest Request(string path, JsonObject artifact, MinecraftManifestSource source) => new(path,
        MinecraftVanillaDownloadPlanner.GetSources(new Uri(artifact["url"]!.GetValue<string>()), source),
        artifact["size"]?.GetValue<long>(), artifact["sha1"]?.GetValue<string>());

    private static void AddLibraries(MinecraftLoaderInstallPlan plan, JsonArray? libraries)
    {
        foreach (var library in libraries?.OfType<JsonObject>() ?? [])
        {
            var artifact = library["downloads"]?["artifact"] as JsonObject;
            string relative = artifact?["path"]?.GetValue<string>() ?? MavenPath(library["name"]!.GetValue<string>());
            string path = Safe(Path.Combine(plan.GameDirectory, "libraries"), relative);
            string? url = artifact?["url"]?.GetValue<string>();
            if (artifact is not null && string.IsNullOrWhiteSpace(url))
            {
                plan.GeneratedFiles.Add((path, artifact["sha1"]?.GetValue<string>()));
                continue;
            }

            url ??= (library["url"]?.GetValue<string>() ?? "https://libraries.minecraft.net/").TrimEnd('/') + "/" + relative;
            var sources = Ordered(url, Mirror + "maven/" + relative, plan.Source);
            plan.Dependencies.Add(new DownloadFileRequest(path, sources, artifact?["size"]?.GetValue<long>() ?? library["size"]?.GetValue<long>(),
                artifact?["sha1"]?.GetValue<string>() ?? library["sha1"]?.GetValue<string>()));
        }
    }
}
