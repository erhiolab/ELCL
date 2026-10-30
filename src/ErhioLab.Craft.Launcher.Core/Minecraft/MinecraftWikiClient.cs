using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>中文 Minecraft Wiki 版本页面的纯文本内容.</summary>
/// <param name="Title">Wiki 页面标题.</param>
/// <param name="Url">可在浏览器打开的页面地址.</param>
/// <param name="Text">页面摘要或完整文字.</param>
public sealed record MinecraftWikiPage(string Title, Uri Url, string Text);

/// <summary>通过 MediaWiki API 读取 Java 版版本页面, 不依赖任何界面技术.</summary>
public sealed class MinecraftWikiClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _apiUrl;

    /// <summary>接收调用方共享的 HTTP 客户端.</summary>
    public MinecraftWikiClient(HttpClient httpClient, Uri? apiUrl = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiUrl = apiUrl ?? new Uri("https://zh.minecraft.wiki/api.php");
    }

    /// <summary>获取版本页面的开头摘要; 页面不存在时返回 null.</summary>
    public Task<MinecraftWikiPage?> GetSummaryAsync(string versionId, CancellationToken cancellationToken = default) => GetPageAsync(versionId, true, cancellationToken);

    /// <summary>获取版本页面的完整纯文本; 页面不存在时返回 null.</summary>
    public Task<MinecraftWikiPage?> GetChangelogAsync(string versionId, CancellationToken cancellationToken = default) => GetPageAsync(versionId, false, cancellationToken);

    /// <summary>构造版本页面地址, 供调用方在浏览器中打开完整内容.</summary>
    public static Uri GetPageUrl(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("版本标识不能为空.", nameof(versionId));
        }

        return new Uri("https://zh.minecraft.wiki/w/" + Uri.EscapeDataString("Java版" + versionId));
    }

    private async Task<MinecraftWikiPage?> GetPageAsync(string versionId, bool introductionOnly,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("版本标识不能为空.", nameof(versionId));
        }

        string title = "Java版" + versionId;
        string query = "action=query&format=json&formatversion=2&redirects=1&prop=extracts&explaintext=1&titles=" + Uri.EscapeDataString(title) + (introductionOnly ? "&exintro=1" : "");
        Uri requestUrl = new(_apiUrl + "?" + query);
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("query", out JsonElement queryResult) ||
            queryResult.ValueKind != JsonValueKind.Object ||
            !queryResult.TryGetProperty("pages", out JsonElement pages)
        )
        {
            throw new JsonException("Wiki API 未返回页面列表.");
        }

        if (pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() == 0)
        {
            throw new JsonException("Wiki API 未返回页面列表.");
        }

        JsonElement page = pages[0];
        if (page.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Wiki API 返回了无效页面.");
        }

        if (page.TryGetProperty("missing", out _))
        {
            return null;
        }

        string pageTitle = page.TryGetProperty("title", out JsonElement titleValue) ? titleValue.GetString() ?? title : title;
        string text = page.TryGetProperty("extract", out JsonElement extract) ? extract.GetString()?.Trim() ?? string.Empty : string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        return new MinecraftWikiPage(pageTitle, new Uri("https://zh.minecraft.wiki/w/" + Uri.EscapeDataString(pageTitle)), text);
    }
}
