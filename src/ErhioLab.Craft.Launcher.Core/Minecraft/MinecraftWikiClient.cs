using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>Minecraft Wiki 版本页面的纯文本内容.</summary>
/// <param name="Title">Wiki 页面标题.</param>
/// <param name="Url">可在浏览器打开的页面地址.</param>
/// <param name="Text">页面摘要或完整文字.</param>
public sealed record MinecraftWikiPage(string Title, Uri Url, string Text);

/// <summary>某个版本页面实际提供的语言及其页面地址.</summary>
public sealed record MinecraftWikiLanguage(string Language, string Title, Uri Url);

/// <summary>通过 MediaWiki API 读取 Java 版版本页面, 不依赖任何界面技术.</summary>
public sealed class MinecraftWikiClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _apiUrl;
    private readonly string _language;

    /// <summary>接收共享 HTTP 客户端/英文 Wiki API 地址及优先语言; 缺少翻译时使用英文页面.</summary>
    public MinecraftWikiClient(HttpClient httpClient, Uri? apiUrl = null, string? language = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _language = NormalizeLanguage(language);
        _apiUrl = apiUrl ?? new Uri("https://minecraft.wiki/api.php");
    }

    /// <summary>获取版本页面的开头摘要; 页面不存在时返回 null.</summary>
    public Task<MinecraftWikiPage?> GetSummaryAsync(string versionId, CancellationToken cancellationToken = default) => GetPageAsync(versionId, true, cancellationToken);

    /// <summary>获取版本页面的完整纯文本; 页面不存在时返回 null.</summary>
    public Task<MinecraftWikiPage?> GetChangelogAsync(string versionId, CancellationToken cancellationToken = default) => GetPageAsync(versionId, false, cancellationToken);

    /// <summary>读取当前版本实际提供的语言列表; 英文页面不存在时返回空列表.</summary>
    public async Task<IReadOnlyList<MinecraftWikiLanguage>> GetLanguagesAsync(string versionId, CancellationToken cancellationToken = default)
    {
        QueryPage? page = await QueryAsync(_apiUrl, EnglishPage(versionId), null, true, cancellationToken).ConfigureAwait(false);
        return page?.Languages ?? [];
    }

    /// <summary>解析当前版本的实际页面地址; 缺少优先语言时回退英文, 英文页面也不存在时返回 null.</summary>
    public async Task<Uri?> GetPageUrlAsync(string versionId, CancellationToken cancellationToken = default) =>
        (await ResolvePageAsync(versionId, null, cancellationToken).ConfigureAwait(false))?.Page.Url;

    /// <summary>仅构造未经验证的候选地址; 无法判断当前版本是否存在翻译.</summary>
    [Obsolete("请使用 GetPageUrlAsync, 按当前版本的跨语言链接解析实际页面地址.")]
    public static Uri GetPageUrl(string versionId, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("版本标识不能为空.", nameof(versionId));
        }

        string code = NormalizeLanguage(language);
        return new Uri(BaseUrl(code), "w/" + Uri.EscapeDataString(PageTitle(versionId, code)));
    }

    private async Task<MinecraftWikiPage?> GetPageAsync(string versionId, bool introductionOnly, CancellationToken cancellationToken)
    {
        QueryPage? page = await ResolvePageAsync(versionId, introductionOnly, cancellationToken).ConfigureAwait(false);
        return page is null || page.Text.Length == 0 ? null : new MinecraftWikiPage(page.Page.Title, page.Page.Url, page.Text);
    }

    private async Task<QueryPage?> ResolvePageAsync(string versionId, bool? introductionOnly, CancellationToken cancellationToken)
    {
        QueryPage? english = await QueryAsync(_apiUrl, EnglishPage(versionId), introductionOnly, _language != "en", cancellationToken).ConfigureAwait(false);
        if (english is null || _language == "en") return english;

        MinecraftWikiLanguage? translation = english.Languages.FirstOrDefault(page => page.Language == _language);
        if (translation is null) return english;

        // 使用跨语言链接给出的实际标题, 不能假定各站点都采用"Java Edition + 版本号".
        return await QueryAsync(new Uri(translation.Url, "/api.php"), translation, introductionOnly, false, cancellationToken).ConfigureAwait(false) ?? english;
    }

    private async Task<QueryPage?> QueryAsync(Uri apiUrl, MinecraftWikiLanguage requestedPage, bool? introductionOnly,
        bool includeLanguages, CancellationToken cancellationToken)
    {
        string properties = "info" + (introductionOnly.HasValue ? "|extracts" : "") + (includeLanguages ? "|langlinks" : "");
        string query = "action=query&format=json&formatversion=2&redirects=1&inprop=url&prop=" + Uri.EscapeDataString(properties)
            + "&titles=" + Uri.EscapeDataString(requestedPage.Title)
            + (introductionOnly.HasValue ? "&explaintext=1" : "")
            + (introductionOnly == true ? "&exintro=1" : "")
            + (includeLanguages ? "&llprop=url&lllimit=max" : "");
        Uri requestUrl = new(apiUrl + (string.IsNullOrEmpty(apiUrl.Query) ? "?" : "&") + query);
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

        if (page.TryGetProperty("missing", out _) || page.TryGetProperty("invalid", out _))
        {
            return null;
        }

        string pageTitle = page.TryGetProperty("title", out JsonElement titleValue) ? titleValue.GetString() ?? requestedPage.Title : requestedPage.Title;
        string text = page.TryGetProperty("extract", out JsonElement extract) ? extract.GetString()?.Trim() ?? string.Empty : string.Empty;
        Uri pageUrl = page.TryGetProperty("fullurl", out JsonElement urlValue)
            && TryWikiUrl(urlValue.GetString(), requestedPage.Language, out Uri? canonicalUrl) ? canonicalUrl! : requestedPage.Url;
        MinecraftWikiLanguage actualPage = new(requestedPage.Language, pageTitle, pageUrl);
        List<MinecraftWikiLanguage> languages = [actualPage];
        if (includeLanguages && page.TryGetProperty("langlinks", out JsonElement links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement link in links.EnumerateArray())
            {
                if (!link.TryGetProperty("lang", out JsonElement codeValue) || !link.TryGetProperty("url", out JsonElement linkUrl)) continue;
                string code = codeValue.GetString()?.ToLowerInvariant() ?? string.Empty;
                if (!TryWikiUrl(linkUrl.GetString(), code, out Uri? url)) continue;
                string? title = link.TryGetProperty("title", out JsonElement linkTitle) ? linkTitle.GetString()
                    : link.TryGetProperty("*", out linkTitle) ? linkTitle.GetString() : null;
                if (!string.IsNullOrWhiteSpace(title) && languages.All(item => item.Language != code))
                    languages.Add(new MinecraftWikiLanguage(code, title, url!));
            }
        }

        return new QueryPage(actualPage, text, languages);
    }

    private static string NormalizeLanguage(string? language)
    {
        string code = language?.Trim().ToLowerInvariant() ?? "en";
        if (code.Length == 0) return "en";
        if (code.Split('-') is not [var first, .. var rest] || first.Length is < 2 or > 3 || !first.All(char.IsAsciiLetterLower)
            || rest.Any(part => part.Length is < 2 or > 8 || !part.All(char.IsAsciiLetterOrDigit)))
            throw new ArgumentOutOfRangeException(nameof(language), language, "Wiki 语言必须是语言代码, 例如 en/zh/lzh 或 pt.");
        return code;
    }

    private static MinecraftWikiLanguage EnglishPage(string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        string title = "Java Edition " + versionId;
        return new MinecraftWikiLanguage("en", title, new Uri(BaseUrl("en"), "w/" + Uri.EscapeDataString(title)));
    }

    private static bool TryWikiUrl(string? value, string language, out Uri? url) =>
        Uri.TryCreate(value, UriKind.Absolute, out url) && url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0
        && url.Host.Equals(language == "en" ? "minecraft.wiki" : $"{language}.minecraft.wiki", StringComparison.OrdinalIgnoreCase);

    private sealed record QueryPage(MinecraftWikiLanguage Page, string Text, IReadOnlyList<MinecraftWikiLanguage> Languages);

    private static Uri BaseUrl(string language) => new(language == "en"
        ? "https://minecraft.wiki/"
        : $"https://{language}.minecraft.wiki/");

    private static string PageTitle(string versionId, string language) => language switch
    {
        "zh" or "lzh" => "Java版" + versionId,
        "de" => versionId,
        "pt" => "Edição Java " + versionId,
        _ => "Java Edition " + versionId
    };
}
