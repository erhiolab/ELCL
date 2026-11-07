using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Accounts;

/// <summary>Minecraft 档案中当前启用的皮肤.</summary>
public sealed record MinecraftPlayerSkin(Uri Url, string? Variant);

/// <summary>玩家拥有的一件披风及其启用状态.</summary>
public sealed record MinecraftPlayerCape(string Id, string Alias, Uri Url, bool IsActive);

/// <summary>玩家档案中的当前皮肤和可选披风.</summary>
public sealed record MinecraftPlayerAppearance(MinecraftPlayerSkin? ActiveSkin, IReadOnlyList<MinecraftPlayerCape> Capes);

/// <summary>读取 Minecraft 档案中的皮肤与披风, 并发送外观修改请求; 缓存位置由调用方决定.</summary>
public sealed class MinecraftSkinClient(HttpClient httpClient)
{
    private const int MaximumTextureBytes = 2 * 1024 * 1024;
    private static readonly Uri ProfileUrl = new("https://api.minecraftservices.com/minecraft/profile");
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public async Task<MinecraftPlayerSkin?> GetActiveSkinAsync(string minecraftAccessToken, CancellationToken cancellationToken = default) => (await GetAppearanceAsync(minecraftAccessToken, cancellationToken).ConfigureAwait(false)).ActiveSkin;

    /// <summary>读取当前皮肤和该账户拥有的披风.</summary>
    public async Task<MinecraftPlayerAppearance> GetAppearanceAsync(string minecraftAccessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftAccessToken);
        using HttpResponseMessage response = await SendWithRetryAsync(() =>
        {
            HttpRequestMessage request = new(HttpMethod.Get, ProfileUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
            return request;
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("skins", out JsonElement skins) || skins.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Minecraft 档案缺少皮肤列表.");

        MinecraftPlayerSkin? activeSkin = null;
        foreach (JsonElement skin in skins.EnumerateArray())
        {
            if (skin.ValueKind != JsonValueKind.Object || !skin.TryGetProperty("state", out JsonElement state) ||
                state.ValueKind != JsonValueKind.String ||
                !string.Equals(state.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase)) continue;
            if (!skin.TryGetProperty("url", out JsonElement url) ||
                url.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? uri))
                throw new InvalidDataException("Minecraft 档案中的皮肤地址无效.");
            uri = NormalizeTextureUri(uri);
            string? variant = skin.TryGetProperty("variant", out JsonElement variantValue) && variantValue.ValueKind == JsonValueKind.String ? variantValue.GetString() : null;
            activeSkin = new MinecraftPlayerSkin(uri, variant);
            break;
        }

        List<MinecraftPlayerCape> capes = [];
        if (document.RootElement.TryGetProperty("capes", out JsonElement capeList) && capeList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement cape in capeList.EnumerateArray())
            {
                if (cape.ValueKind != JsonValueKind.Object ||
                    !cape.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.String ||
                    !cape.TryGetProperty("url", out JsonElement url) || url.ValueKind != JsonValueKind.String ||
                    !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? capeUrl)) continue;
                string? capeId = id.GetString();
                if (string.IsNullOrWhiteSpace(capeId)) continue;
                string alias = cape.TryGetProperty("alias", out JsonElement aliasValue) && aliasValue.ValueKind == JsonValueKind.String ? aliasValue.GetString() ?? capeId : capeId;
                bool active = cape.TryGetProperty("state", out JsonElement capeState) && capeState.ValueKind == JsonValueKind.String && string.Equals(capeState.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
                try
                {
                    capes.Add(new MinecraftPlayerCape(capeId, alias, NormalizeTextureUri(capeUrl), active));
                }
                catch (InvalidDataException)
                {
                    // 跳过损坏的披风条目, 不影响当前皮肤的读取.
                }
            }
        }

        return new MinecraftPlayerAppearance(activeSkin, capes);
    }

    public async Task<byte[]> DownloadSkinAsync(MinecraftPlayerSkin skin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skin);
        return await DownloadTextureAsync(skin.Url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>下载披风 PNG; 缓存位置由调用方决定.</summary>
    public Task<byte[]> DownloadCapeAsync(MinecraftPlayerCape cape, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cape);
        return DownloadTextureAsync(cape.Url, cancellationToken);
    }

    /// <summary>上传 PNG 皮肤; variant 为 CLASSIC 或 SLIM.</summary>
    public async Task UploadSkinAsync(string minecraftAccessToken, byte[] png, string variant,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftAccessToken);
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length > MaximumTextureBytes || !png.AsSpan().StartsWith(PngSignature))
            throw new InvalidDataException("皮肤文件必须是大小不超过 2 MiB 的 PNG.");
        if (variant is not ("CLASSIC" or "SLIM")) throw new ArgumentException("皮肤模型必须是 CLASSIC 或 SLIM.", nameof(variant));

        using HttpResponseMessage response = await SendWithRetryAsync(() =>
        {
            HttpRequestMessage request = new(HttpMethod.Post, new Uri(ProfileUrl, "profile/skins"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
            MultipartFormDataContent form = new();
            ByteArrayContent file = new(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "skin.png");
            form.Add(new StringContent(variant, Encoding.UTF8), "variant");
            request.Content = form;
            return request;
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>启用指定披风; 传入 null 时取消装备披风.</summary>
    public async Task SetActiveCapeAsync(string minecraftAccessToken, string? capeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftAccessToken);
        if (capeId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(capeId);
        using HttpResponseMessage response = await SendWithRetryAsync(() =>
        {
            HttpRequestMessage request = new(capeId is null ? HttpMethod.Delete : HttpMethod.Put,
                new Uri(ProfileUrl, "profile/capes/active"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
            if (capeId is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(new { capeId }), Encoding.UTF8, "application/json");
            return request;
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<byte[]> DownloadTextureAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("材质地址必须使用 HTTPS.", nameof(uri));
        using HttpResponseMessage response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumTextureBytes) throw new InvalidDataException("材质文件超过大小限制.");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream output = new();
        byte[] buffer = new byte[32 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > MaximumTextureBytes) throw new InvalidDataException("材质文件超过大小限制.");
            output.Write(buffer, 0, read);
        }

        byte[] bytes = output.ToArray();
        if (!bytes.AsSpan().StartsWith(PngSignature)) throw new InvalidDataException("材质文件不是 PNG.");
        return bytes;
    }

    private static Uri NormalizeTextureUri(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttp &&
            uri.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort)
            uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Minecraft 档案中的材质地址无效.");
        return uri;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0;; attempt++)
        {
            using HttpRequestMessage request = createRequest();
            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (attempt < 2 && (status is 408 or 429 || status >= 500))
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 400 : 900), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException error) when (attempt < 2 && error.HttpRequestError is HttpRequestError.SecureConnectionError or HttpRequestError.ConnectionError)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 400 : 900), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
