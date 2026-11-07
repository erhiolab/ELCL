using System.Net.Http.Headers;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Accounts;

/// <summary>Minecraft 档案中当前启用的皮肤.</summary>
public sealed record MinecraftPlayerSkin(Uri Url, string? Variant);

/// <summary>读取 Minecraft 档案的当前皮肤并下载 PNG；缓存位置由调用方决定.</summary>
public sealed class MinecraftSkinClient(HttpClient httpClient)
{
    private const int MaximumSkinBytes = 2 * 1024 * 1024;
    private static readonly Uri ProfileUrl = new("https://api.minecraftservices.com/minecraft/profile");
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public async Task<MinecraftPlayerSkin?> GetActiveSkinAsync(string minecraftAccessToken,
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

        foreach (JsonElement skin in skins.EnumerateArray())
        {
            if (skin.ValueKind != JsonValueKind.Object || !skin.TryGetProperty("state", out JsonElement state) ||
                state.ValueKind != JsonValueKind.String ||
                !string.Equals(state.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase)) continue;
            if (!skin.TryGetProperty("url", out JsonElement url) ||
                url.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? uri))
                throw new InvalidDataException("Minecraft 档案中的皮肤地址无效.");
            if (uri.Scheme == Uri.UriSchemeHttp &&
                uri.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort)
                uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Minecraft 档案中的皮肤地址无效.");
            string? variant = skin.TryGetProperty("variant", out JsonElement variantValue) && variantValue.ValueKind == JsonValueKind.String ? variantValue.GetString() : null;
            return new MinecraftPlayerSkin(uri, variant);
        }

        return null;
    }

    public async Task<byte[]> DownloadSkinAsync(MinecraftPlayerSkin skin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (skin.Url.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("皮肤地址必须使用 HTTPS.", nameof(skin));
        using HttpResponseMessage response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, skin.Url),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumSkinBytes) throw new InvalidDataException("皮肤文件超过大小限制.");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream output = new();
        byte[] buffer = new byte[32 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > MaximumSkinBytes) throw new InvalidDataException("皮肤文件超过大小限制.");
            output.Write(buffer, 0, read);
        }

        byte[] bytes = output.ToArray();
        if (!bytes.AsSpan().StartsWith(PngSignature)) throw new InvalidDataException("皮肤文件不是 PNG.");
        return bytes;
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
