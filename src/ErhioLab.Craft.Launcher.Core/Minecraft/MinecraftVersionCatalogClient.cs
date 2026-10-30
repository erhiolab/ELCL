using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>两个来源均无法返回有效版本清单时抛出的异常.</summary>
public sealed class MinecraftVersionCatalogException : Exception
{
    /// <summary>保留两个来源的错误, 便于调用方记录原因.</summary>
    public MinecraftVersionCatalogException(Exception preferred, Exception fallback) : base("官方源和镜像源均未能返回有效的 Minecraft 版本列表.", new AggregateException(preferred, fallback))
    {
    }
}

/// <summary>获取 Mojang 版本清单, 超时或无效时切换到另一来源.</summary>
public sealed class MinecraftVersionCatalogClient
{
    /// <summary>官方版本清单地址.</summary>
    public static readonly Uri OfficialManifestUrl = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    /// <summary>BMCLAPI 镜像清单地址.</summary>
    public static readonly Uri MirrorManifestUrl = new("https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json");

    private static readonly HashSet<string> AprilFoolsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "1.RV-Pre1", "3D Shareware v1.34", "15w14a", "20w14infinite",
        "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine", "26w14a"
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _officialUrl;
    private readonly Uri _mirrorUrl;
    private readonly TimeSpan _sourceTimeout;

    /// <summary>使用调用方提供的 HTTP 客户端, 便于共享连接和替换来源进行测试.</summary>
    public MinecraftVersionCatalogClient(HttpClient httpClient, Uri? officialUrl = null, Uri? mirrorUrl = null, TimeSpan? sourceTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _officialUrl = officialUrl ?? OfficialManifestUrl;
        _mirrorUrl = mirrorUrl ?? MirrorManifestUrl;
        _sourceTimeout = sourceTimeout ?? TimeSpan.FromSeconds(5);
        if (_sourceTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceTimeout));
        }
    }

    /// <summary>优先尝试指定来源; 它超时、失败或返回无效数据时再尝试另一来源.</summary>
    public async Task<MinecraftVersionCatalog> GetCatalogAsync(MinecraftManifestSource preferredSource, CancellationToken cancellationToken = default)
    {
        MinecraftManifestSource fallbackSource = preferredSource == MinecraftManifestSource.Official ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
        try
        {
            return await FetchAsync(preferredSource, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception preferredError) when (IsSourceFailure(preferredError, cancellationToken))
        {
            try
            {
                return await FetchAsync(fallbackSource, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception fallbackError) when (IsSourceFailure(fallbackError, cancellationToken))
            {
                throw new MinecraftVersionCatalogException(preferredError, fallbackError);
            }
        }
    }

    /// <summary>解析现有清单 JSON, 可独立于网络调用.</summary>
    public static MinecraftVersionCatalog Parse(string json, MinecraftManifestSource source)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement latest = RequiredProperty(root, "latest");
        string latestRelease = RequiredString(latest, "release");
        string latestPreview = RequiredString(latest, "snapshot");
        JsonElement entries = RequiredProperty(root, "versions");
        if (entries.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("版本列表不是数组.");
        }

        List<MinecraftVersion> versions = [];
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out JsonElement idValue)
                || !entry.TryGetProperty("type", out JsonElement typeValue)
                || !entry.TryGetProperty("url", out JsonElement urlValue)
                || !entry.TryGetProperty("releaseTime", out JsonElement releaseTimeValue)
                || idValue.ValueKind != JsonValueKind.String
                || typeValue.ValueKind != JsonValueKind.String
                || urlValue.ValueKind != JsonValueKind.String
                || releaseTimeValue.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? id = idValue.GetString();
            string? type = typeValue.GetString();
            string? url = urlValue.GetString();
            string? releaseTime = releaseTimeValue.GetString();
            if (
                string.IsNullOrWhiteSpace(id) ||
                !seenIds.Add(id) ||
                !Uri.TryCreate(url, UriKind.Absolute, out Uri? metadataUrl) ||
                metadataUrl.Scheme != Uri.UriSchemeHttps ||
                !DateTimeOffset.TryParse(releaseTime, out DateTimeOffset published)
            )
            {
                continue;
            }

            MinecraftVersionKind? kind = type switch
            {
                "release" => MinecraftVersionKind.Release,
                "snapshot" when AprilFoolsIds.Contains(id) || published.Month == 4 && published.Day == 1 => MinecraftVersionKind.AprilFools,
                "snapshot" => MinecraftVersionKind.Preview,
                "old_alpha" or "old_beta" => MinecraftVersionKind.Ancient,
                _ => null
            };
            if (kind is null)
            {
                continue;
            }

            string? sha1 = entry.TryGetProperty("sha1", out JsonElement hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
            versions.Add(new MinecraftVersion(id, kind.Value, metadataUrl, published, sha1));
        }

        if (versions.Count == 0 || !versions.Any(version => version.Id == latestRelease) || !versions.Any(version => version.Id == latestPreview))
        {
            throw new JsonException("清单没有有效版本或缺少最新版本.");
        }

        return new MinecraftVersionCatalog(source, latestRelease, latestPreview, versions);
    }

    private async Task<MinecraftVersionCatalog> FetchAsync(MinecraftManifestSource source,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_sourceTimeout);
        Uri uri = source == MinecraftManifestSource.Official ? _officialUrl : _mirrorUrl;
        using HttpResponseMessage response = await _httpClient.GetAsync(uri, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return Parse(json, source);
    }

    private static bool IsSourceFailure(Exception error, CancellationToken callerToken) => error is HttpRequestException or JsonException || error is OperationCanceledException && !callerToken.IsCancellationRequested;

    private static JsonElement RequiredProperty(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement property) ? property : throw new JsonException($"清单缺少 {name} 字段.");

    private static string RequiredString(JsonElement parent, string name)
    {
        JsonElement value = RequiredProperty(parent, name);
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new JsonException($"清单中的 {name} 不是有效字符串.");
    }
}
