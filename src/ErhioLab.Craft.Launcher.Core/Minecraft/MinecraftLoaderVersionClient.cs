using System.Net;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>两个来源均无法提供有效加载器版本列表时抛出的异常.</summary>
public sealed class MinecraftLoaderVersionException : Exception
{
    /// <summary>保留两个来源的失败原因.</summary>
    public MinecraftLoaderVersionException(MinecraftLoaderKind kind, string gameVersion, Exception preferred, Exception fallback)
        : base($"无法获取 {gameVersion} 的 {kind} 版本列表.", new AggregateException(preferred, fallback))
    {
    }
}

/// <summary>获取 Forge, NeoForge, Fabric, Quilt, LiteLoader 和 OptiFine 的版本列表.</summary>
public sealed class MinecraftLoaderVersionClient
{
    private const string ForgeOfficial = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string ForgeMirror = "https://bmclapi2.bangbang93.com/forge/minecraft/";
    private const string NeoForgeOfficial = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const string NeoForgeLegacyOfficial = "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml";
    private const string NeoForgeMirror = "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge";
    private const string NeoForgeLegacyMirror = "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge";
    private const string FabricOfficial = "https://meta.fabricmc.net/v2/versions/loader/";
    private const string FabricMirror = "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/";
    private const string OptiFineOfficial = "https://optifine.net/downloads";
    private const string OptiFineMirror = "https://bmclapi2.bangbang93.com/optifine/versionList";
    private const string QuiltOfficial = "https://meta.quiltmc.org/v3/versions/loader/";
    private const string QuiltMirror = "https://bmclapi2.bangbang93.com/quilt-meta/v3/versions/loader/";
    private const string QuiltMaven = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-loader/";
    private const string LiteLoaderOfficial = "https://dl.liteloader.com/versions/versions.json";
    private const string LiteLoaderMirror = "https://bmclapi2.bangbang93.com/liteloader/list?mcversion=";
    private const string ForgeDownloads = "https://files.minecraftforge.net/net/minecraftforge/forge/index_";
    private const string FabricMaven = "https://maven.fabricmc.net/net/fabricmc/fabric-loader/";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _sourceTimeout;
    private static readonly ConcurrentDictionary<string, DateOnly> QuiltDateCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>由调用方提供 HTTP 客户端, 便于共享连接和替换网络响应.</summary>
    public MinecraftLoaderVersionClient(HttpClient httpClient, TimeSpan? sourceTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sourceTimeout = sourceTimeout ?? TimeSpan.FromSeconds(8);
        if (_sourceTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceTimeout));
        }
    }

    /// <summary>按优先来源获取版本, 无结果或来源失败时尝试另一来源.</summary>
    public async Task<MinecraftLoaderVersionCatalog> GetVersionsAsync(
        MinecraftLoaderKind kind,
        string gameVersion,
        MinecraftManifestSource preferredSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(preferredSource))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        MinecraftManifestSource fallbackSource = preferredSource == MinecraftManifestSource.Official
            ? MinecraftManifestSource.Mirror
            : MinecraftManifestSource.Official;
        MinecraftLoaderVersionCatalog? emptyResult = null;
        Exception? preferredError = null;
        Exception? fallbackError = null;

        foreach (MinecraftManifestSource source in new[] { preferredSource, fallbackSource })
        {
            try
            {
                MinecraftLoaderVersionCatalog catalog = await FetchAsync(kind, gameVersion, source, cancellationToken).ConfigureAwait(false);
                if (catalog.Versions.Count > 0)
                {
                    return catalog;
                }

                emptyResult ??= catalog;
            }
            catch (Exception error) when (IsSourceFailure(error, cancellationToken))
            {
                if (source == preferredSource)
                {
                    preferredError = error;
                }
                else
                {
                    fallbackError = error;
                }
            }
        }

        return emptyResult ?? throw new MinecraftLoaderVersionException(kind, gameVersion, preferredError!, fallbackError!);
    }

    /// <summary>从 Forge 镜像取得逐版本更新时间, 可用于补充官方 Maven 列表缺少的日期.</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> GetForgeVersionDatesAsync(
        string gameVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        MinecraftLoaderVersionCatalog mirror = await FetchAsync(
            MinecraftLoaderKind.Forge,
            gameVersion,
            MinecraftManifestSource.Mirror,
            cancellationToken).ConfigureAwait(false);
        return mirror.Versions
            .Where(version => version.LastModifiedDate.HasValue)
            .ToDictionary(version => version.Version, version => version.LastModifiedDate!.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>从 Forge 官方下载页解析版本发布时间.</summary>
    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetForgeReleaseTimesAsync(
        string gameVersion,
        IReadOnlyCollection<string> versions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0)
        {
            return new Dictionary<string, DateTimeOffset>();
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_sourceTimeout);
        using HttpResponseMessage response = await _httpClient.GetAsync(
            ForgeDownloads + Uri.EscapeDataString(gameVersion.Replace('-', '_')) + ".html",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        HashSet<string> requested = versions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DateTimeOffset> dates = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2)))
        {
            Match versionMatch = Regex.Match(row.Value, @"<td\b[^>]*class=[""']download-version[""'][^>]*>\s*(?<version>[^<\s]+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            Match timeMatch = Regex.Match(row.Value, @"<td\b[^>]*class=[""']download-time[""'][^>]*title=[""'](?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})[""']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (!versionMatch.Success || !timeMatch.Success || !requested.Contains(versionMatch.Groups["version"].Value)
                || !DateTime.TryParseExact(timeMatch.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                continue;
            }

            dates.TryAdd(versionMatch.Groups["version"].Value, new DateTimeOffset(parsed, TimeSpan.Zero));
        }

        return dates;
    }

    /// <summary>从 Fabric 官方 Maven 目录读取构件目录的更新日期.</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> GetFabricVersionDatesAsync(
        IReadOnlyCollection<string> versions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0)
        {
            return new Dictionary<string, DateOnly>();
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_sourceTimeout);
        using HttpResponseMessage response = await _httpClient.GetAsync(FabricMaven, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        HashSet<string> requested = versions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DateOnly> dates = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in Regex.Matches(html, @"<a\s+href=[""'](?<version>[^/""']+)/[""'][^>]*>[^<]*</a>\s*(?<date>\d{2}-[A-Za-z]{3}-\d{4})", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
        {
            string version = row.Groups["version"].Value;
            if (requested.Contains(version)
                && DateOnly.TryParseExact(row.Groups["date"].Value, "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                dates.TryAdd(version, date);
            }
        }

        return dates;
    }

    /// <summary>读取 Quilt 官方 Maven 构件目录的原始更新时间, 单个版本失败不影响其他版本.</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> GetQuiltVersionDatesAsync(
        IReadOnlyCollection<string> versions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ConcurrentDictionary<string, DateOnly> dates = new(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(versions.Distinct(StringComparer.OrdinalIgnoreCase),
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken }, async (version, token) =>
            {
                if (QuiltDateCache.TryGetValue(version, out DateOnly cached))
                {
                    dates.TryAdd(version, cached);
                    return;
                }

                try
                {
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(_sourceTimeout);
                    string url = QuiltMaven + Uri.EscapeDataString(version) + "/";
                    using HttpRequestMessage request = new(HttpMethod.Head, url);
                    using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode
                        || !response.Headers.TryGetValues("quilt-last-modified-timestamp", out IEnumerable<string>? values)
                        || !long.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, out long milliseconds))
                    {
                        return;
                    }

                    DateOnly date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
                    QuiltDateCache.TryAdd(version, date);
                    dates.TryAdd(version, date);
                }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException or ArgumentOutOfRangeException)
                {
                    // 日期只是附加信息; 超时或单个目录缺失时保留原版本列表.
                    token.ThrowIfCancellationRequested();
                }
            }).ConfigureAwait(false);

        return dates;
    }

    private async Task<MinecraftLoaderVersionCatalog> FetchAsync(
        MinecraftLoaderKind kind,
        string gameVersion,
        MinecraftManifestSource source,
        CancellationToken cancellationToken)
    {
        string? neoForgePrefix = kind == MinecraftLoaderKind.NeoForge ? NeoForgePrefix(gameVersion) : null;
        if (kind == MinecraftLoaderKind.NeoForge && neoForgePrefix is null)
        {
            return new MinecraftLoaderVersionCatalog(kind, gameVersion, source, []);
        }

        string url = GetUrl(kind, gameVersion, source);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_sourceTimeout);
        using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        // Fabric 对不支持的游戏版本可能返回官方 400、镜像 404; 这是无可用版本.
        bool unsupportedFabric = kind == MinecraftLoaderKind.Fabric
                                 && (response.StatusCode == HttpStatusCode.NotFound
                                     || source == MinecraftManifestSource.Official && response.StatusCode == HttpStatusCode.BadRequest);
        bool unsupportedQuilt = kind == MinecraftLoaderKind.Quilt
                                && source == MinecraftManifestSource.Official
                                && response.StatusCode == HttpStatusCode.NotFound;
        if (unsupportedFabric || unsupportedQuilt)
        {
            return new MinecraftLoaderVersionCatalog(kind, gameVersion, source, []);
        }

        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        IReadOnlyList<MinecraftLoaderVersion> versions = kind switch
        {
            MinecraftLoaderKind.Forge when source == MinecraftManifestSource.Official => ParseMaven(body, value => value.StartsWith(gameVersion + "-", StringComparison.OrdinalIgnoreCase) ? value[(gameVersion.Length + 1)..] : null),
            MinecraftLoaderKind.Forge => ParseForgeMirror(body, gameVersion),
            MinecraftLoaderKind.NeoForge when source == MinecraftManifestSource.Official => ParseMaven(body, value => NeoForgeMatches(value, neoForgePrefix!, gameVersion) ? gameVersion == "1.20.1" ? value[(gameVersion.Length + 1)..] : value : null),
            MinecraftLoaderKind.NeoForge => ParseNeoForgeMirror(body, neoForgePrefix!, gameVersion),
            MinecraftLoaderKind.Fabric => ParseFabric(body),
            MinecraftLoaderKind.Quilt => ParseQuilt(body),
            MinecraftLoaderKind.LiteLoader when source == MinecraftManifestSource.Official => ParseLiteLoaderOfficial(body, gameVersion),
            MinecraftLoaderKind.LiteLoader => ParseLiteLoaderMirror(body, gameVersion),
            MinecraftLoaderKind.OptiFine when source == MinecraftManifestSource.Official => ParseOptiFineOfficial(body, gameVersion),
            MinecraftLoaderKind.OptiFine => ParseOptiFineMirror(body, gameVersion),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new MinecraftLoaderVersionCatalog(kind, gameVersion, source, SortVersions(versions));
    }

    private static string GetUrl(MinecraftLoaderKind kind, string gameVersion, MinecraftManifestSource source)
    {
        bool official = source == MinecraftManifestSource.Official;
        return kind switch
        {
            MinecraftLoaderKind.Forge => official ? ForgeOfficial : ForgeMirror + Uri.EscapeDataString(gameVersion.Replace('-', '_')),
            MinecraftLoaderKind.NeoForge => gameVersion == "1.20.1"
                ? official ? NeoForgeLegacyOfficial : NeoForgeLegacyMirror
                : official
                    ? NeoForgeOfficial
                    : NeoForgeMirror,
            MinecraftLoaderKind.Fabric => (official ? FabricOfficial : FabricMirror) + Uri.EscapeDataString(gameVersion),
            MinecraftLoaderKind.Quilt => (official ? QuiltOfficial : QuiltMirror) + Uri.EscapeDataString(gameVersion),
            MinecraftLoaderKind.LiteLoader => official ? LiteLoaderOfficial : LiteLoaderMirror + Uri.EscapeDataString(gameVersion),
            MinecraftLoaderKind.OptiFine => official ? OptiFineOfficial : OptiFineMirror,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseMaven(string xml, Func<string, string?> map)
    {
        XDocument document = XDocument.Parse(xml);
        XElement? versions = document.Root?.Element("versioning")?.Element("versions");
        if (document.Root?.Name.LocalName != "metadata" || versions is null || !versions.Elements("version").Any())
        {
            throw new XmlException("Maven 元数据缺少版本列表.");
        }

        return versions.Elements("version")
            .Select(element => map(element.Value.Trim()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new MinecraftLoaderVersion(value!, IsPreview(value!)))
            .DistinctBy(entry => entry.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseForgeMirror(string json, string gameVersion)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entries = RequireArray(document.RootElement);
        List<MinecraftLoaderVersion> versions = [];
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (GetString(entry, "mcversion") != gameVersion || GetString(entry, "version") is not { } version)
            {
                continue;
            }

            DateOnly? modified = null;
            if (GetString(entry, "modified") is { } modifiedText
                && DateTimeOffset.TryParse(modifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp))
            {
                modified = DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime);
            }

            versions.Add(new MinecraftLoaderVersion(version, IsPreview(version), LastModifiedDate: modified));
        }

        return versions;
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseNeoForgeMirror(string json, string prefix, string gameVersion)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("files", out JsonElement files))
        {
            throw new JsonException("NeoForge 镜像数据缺少 files 字段.");
        }

        List<MinecraftLoaderVersion> versions = [];
        foreach (JsonElement entry in RequireArray(files).EnumerateArray())
        {
            if (GetString(entry, "name") is not { } name || !NeoForgeMatches(name, prefix, gameVersion))
            {
                continue;
            }

            string version = gameVersion == "1.20.1" ? name[(gameVersion.Length + 1)..] : name;
            versions.Add(new MinecraftLoaderVersion(version, IsPreview(version)));
        }

        return versions;
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseFabric(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<MinecraftLoaderVersion> versions = [];
        foreach (JsonElement entry in RequireArray(document.RootElement).EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("loader", out JsonElement loader) || GetString(loader, "version") is not { } version)
            {
                continue;
            }

            bool stable = loader.TryGetProperty("stable", out JsonElement stableValue) && stableValue.ValueKind == JsonValueKind.True;
            versions.Add(new MinecraftLoaderVersion(version, !stable));
        }

        return versions;
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseQuilt(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<MinecraftLoaderVersion> versions = [];
        foreach (JsonElement entry in RequireArray(document.RootElement).EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("loader", out JsonElement loader)
                && GetString(loader, "version") is { } version)
            {
                versions.Add(new MinecraftLoaderVersion(version, IsPreview(version)));
            }
        }

        return versions;
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseLiteLoaderOfficial(string json, string gameVersion)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("versions", out JsonElement allVersions) || allVersions.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("LiteLoader 官方元数据缺少 versions 字段.");
        }

        if (!allVersions.TryGetProperty(gameVersion, out JsonElement game) || game.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        List<MinecraftLoaderVersion> versions = [];
        foreach (string groupName in new[] { "artefacts", "snapshots" })
        {
            if (!game.TryGetProperty(groupName, out JsonElement group)
                || !group.TryGetProperty("com.mumfrey:liteloader", out JsonElement builds)
                || builds.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty build in builds.EnumerateObject())
            {
                if (build.Name == "latest" || GetString(build.Value, "version") is not { } version)
                {
                    continue;
                }

                bool preview = GetString(build.Value, "stream")?.Equals("SNAPSHOT", StringComparison.OrdinalIgnoreCase) == true;
                versions.Add(new MinecraftLoaderVersion(version, preview || IsPreview(version), BuildDate: ParseUnixDate(GetString(build.Value, "timestamp"))));
            }
        }

        return versions;
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseLiteLoaderMirror(string json, string gameVersion)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<MinecraftLoaderVersion> versions = [];
        foreach (JsonElement entry in RequireArray(document.RootElement).EnumerateArray())
        {
            if (GetString(entry, "mcversion") != gameVersion || GetString(entry, "version") is not { } version)
            {
                continue;
            }

            bool preview = GetString(entry, "type")?.Equals("SNAPSHOT", StringComparison.OrdinalIgnoreCase) == true;
            DateOnly? released = entry.TryGetProperty("build", out JsonElement build)
                ? ParseUnixDate(GetString(build, "timestamp"))
                : null;
            versions.Add(new MinecraftLoaderVersion(version, preview || IsPreview(version), BuildDate: released));
        }

        return versions;
    }

    private static DateOnly? ParseUnixDate(string? value)
    {
        return long.TryParse(value, CultureInfo.InvariantCulture, out long seconds)
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().DateTime)
            : null;
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseOptiFineOfficial(string html, string gameVersion)
    {
        if (!html.Contains("OptiFine_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OptiFine 官方页面没有版本内容.");
        }

        string filePattern = "(?<preview>preview_)?OptiFine_" + Regex.Escape(gameVersion) + @"_(?<edition>[A-Za-z0-9_]+)\.jar";
        return Regex.Matches(html, @"<tr\b[^>]*class=['\""][^'\""]*downloadLine[^'\""]*['\""][^>]*>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2))
            .Select(row =>
            {
                Match file = Regex.Match(row.Value, filePattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                if (!file.Success)
                {
                    return null;
                }

                Match date = Regex.Match(row.Value, @"<td\b[^>]*class=['\""]colDate['\""][^>]*>\s*(?<date>\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                DateOnly? releaseDate = date.Success
                                        && DateOnly.TryParseExact(date.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
                    ? parsed
                    : null;
                string edition = file.Groups["edition"].Value;
                Match forge = Regex.Match(row.Value, @"<td\b[^>]*class=['\""]colForge['\""][^>]*>\s*(?<version>[^<]*)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                return new MinecraftLoaderVersion(edition.Replace('_', ' '), file.Groups["preview"].Success || IsPreview(edition), releaseDate,
                    RequiredForgeVersion: ParseForgeRequirement(forge.Success ? forge.Groups["version"].Value : null));
            })
            .Where(version => version is not null)
            .Select(version => version!)
            .DistinctBy(entry => entry.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MinecraftLoaderVersion> ParseOptiFineMirror(string json, string gameVersion)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<MinecraftLoaderVersion> versions = [];
        foreach (JsonElement entry in RequireArray(document.RootElement).EnumerateArray())
        {
            if (GetString(entry, "mcversion") != gameVersion || GetString(entry, "patch") is not { } patch)
            {
                continue;
            }

            string type = GetString(entry, "type")?.Replace('_', ' ') ?? "HD U";
            versions.Add(new MinecraftLoaderVersion(type + " " + patch.Replace('_', ' '), IsPreview(patch),
                RequiredForgeVersion: ParseForgeRequirement(GetString(entry, "forge"))));
        }

        return versions;
    }

    private static string? ParseForgeRequirement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.Replace("Forge", "", StringComparison.OrdinalIgnoreCase).Replace("#", "").Trim();
    }

    private static IReadOnlyList<MinecraftLoaderVersion> SortVersions(IReadOnlyList<MinecraftLoaderVersion> versions) => versions
        .DistinctBy(entry => entry.Version, StringComparer.OrdinalIgnoreCase)
        .OrderBy(entry => entry.IsPreview)
        .ThenByDescending(entry => entry.Version, Comparer<string>.Create(CompareNatural))
        .ToArray();

    private static int CompareNatural(string left, string right)
    {
        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
            {
                int leftEnd = leftIndex;
                int rightEnd = rightIndex;
                while (leftEnd < left.Length && char.IsDigit(left[leftEnd])) leftEnd++;
                while (rightEnd < right.Length && char.IsDigit(right[rightEnd])) rightEnd++;
                ReadOnlySpan<char> leftNumber = left.AsSpan(leftIndex, leftEnd - leftIndex).TrimStart('0');
                ReadOnlySpan<char> rightNumber = right.AsSpan(rightIndex, rightEnd - rightIndex).TrimStart('0');
                int numberOrder = leftNumber.Length.CompareTo(rightNumber.Length);
                if (numberOrder == 0) numberOrder = leftNumber.CompareTo(rightNumber, StringComparison.Ordinal);
                if (numberOrder != 0) return numberOrder;
                leftIndex = leftEnd;
                rightIndex = rightEnd;
                continue;
            }

            int order = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (order != 0) return order;
            leftIndex++;
            rightIndex++;
        }

        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }

    private static string? NeoForgePrefix(string gameVersion)
    {
        // 1.20.1 使用旧 Maven 坐标; 愚人节版本使用 0. 前缀.
        if (gameVersion == "1.20.1") return gameVersion + "-";
        if (Regex.IsMatch(gameVersion, @"^\d{2}w\d{2}[a-z]+$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            return "0." + gameVersion + ".";
        }

        string[] parts = gameVersion.Split('-', 2)[0].Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor)) return null;
        if (major == 1)
        {
            int patch = parts.Length > 2 && int.TryParse(parts[2], out int value) ? value : 0;
            return $"{minor}.{patch}.";
        }

        if (major < 24) return null;
        int build = parts.Length > 2 && int.TryParse(parts[2], out int segment) ? segment : 0;
        return $"{major}.{minor}.{build}.";
    }

    private static bool NeoForgeMatches(string artifact, string prefix, string gameVersion)
    {
        if (!artifact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 快照和正式版可能共享数字前缀, 还需核对 +snapshot-N 后缀.
        int snapshotMarker = gameVersion.IndexOf("-snapshot-", StringComparison.OrdinalIgnoreCase);
        return snapshotMarker >= 0 ? artifact.EndsWith("+" + gameVersion[(snapshotMarker + 1)..], StringComparison.OrdinalIgnoreCase) : !artifact.Contains("+snapshot-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPreview(string version) => version.Contains("alpha", StringComparison.OrdinalIgnoreCase) || version.Contains("beta", StringComparison.OrdinalIgnoreCase) || version.Contains("pre", StringComparison.OrdinalIgnoreCase) || version.Contains("rc", StringComparison.OrdinalIgnoreCase);

    private static JsonElement RequireArray(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value : throw new JsonException("加载器版本列表不是数组.");

    private static string? GetString(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsSourceFailure(Exception error, CancellationToken callerToken) => error is HttpRequestException or JsonException or XmlException or InvalidDataException or RegexMatchTimeoutException || error is OperationCanceledException && !callerToken.IsCancellationRequested;
}
