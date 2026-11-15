using System.Net;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Mods;

/// <summary>从 MC 百科公开页面解析译名; 未能核实项目时返回 null.</summary>
public sealed class McModTranslationClient(HttpClient httpClient)
{
    private readonly HttpClient _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private static readonly SemaphoreSlim SearchGate = new(1);
    private static long _nextSearch;

    public async Task<McModTranslation?> FindAsync(string name, string? curseForgeSlug = null, string? modrinthSlug = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        if (McModTranslationIndex.Find(name, curseForgeSlug, modrinthSlug) is { } indexed) return indexed;
        string html;
        await SearchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long wait = _nextSearch - Environment.TickCount64;
            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellationToken).ConfigureAwait(false);
            html = await ReadAsync(new Uri("https://search.mcmod.cn/s?filter=1&key=" + Uri.EscapeDataString(name)), cancellationToken).ConfigureAwait(false);
            _nextSearch = Environment.TickCount64 + 2000;
            if (html.Contains("搜索太频繁", StringComparison.Ordinal))
                throw new HttpRequestException("MC 百科搜索暂时限流, 请稍后重试.", null, HttpStatusCode.TooManyRequests);
        }
        finally
        {
            SearchGate.Release();
        }

        var candidates = Regex.Matches(html, "href=[\"'](https://www\\.mcmod\\.cn/class/[0-9]+\\.html)[\"'][^>]*>(.*?)</a>", RegexOptions.Singleline)
            .Select(match => (Uri: match.Groups[1].Value, Name: Plain(match.Groups[2].Value)))
            .DistinctBy(value => value.Uri).OrderBy(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase) ? 0
                : Regex.IsMatch(value.Name, @"[（(]\s*" + Regex.Escape(name) + @"\s*[）)]$", RegexOptions.IgnoreCase) ? 1
                : value.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ? 2 : 3).Take(4);
        foreach (var candidate in candidates)
        {
            string page = await ReadAsync(new Uri(candidate.Uri), cancellationToken).ConfigureAwait(false);
            var title = Regex.Match(page, "class=[\"']class-title[\"'][^>]*>.*?</h3>(?:\\s*<h4[^>]*>.*?</h4>)?", RegexOptions.Singleline).Value;
            string chinese = Plain(Regex.Match(title, "<h3[^>]*>(.*?)</h3>", RegexOptions.Singleline).Groups[1].Value);
            string english = Plain(Regex.Match(title, "<h4[^>]*>(.*?)(?:</h4>|$)", RegexOptions.Singleline).Groups[1].Value);
            string? cf = SourceSlug(page, "curseforge", "curseforge\\.com/minecraft/mc-mods/"), mr = SourceSlug(page, "modrinth", "modrinth\\.com/mod/");
            bool matches = curseForgeSlug is not null && string.Equals(cf, curseForgeSlug, StringComparison.OrdinalIgnoreCase)
                           || modrinthSlug is not null && string.Equals(mr, modrinthSlug, StringComparison.OrdinalIgnoreCase)
                           || curseForgeSlug is null && modrinthSlug is null && (string.Equals(english, name, StringComparison.OrdinalIgnoreCase) || string.Equals(chinese, name, StringComparison.OrdinalIgnoreCase));
            if (matches && chinese.Length > 0) return new McModTranslation(chinese, english, new Uri(candidate.Uri), cf, mr);
        }

        return null;
    }

    private async Task<string> ReadAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("ELCLC/" + CoreInfo.Version + " (https://github.com/ErhioLab/ELCL)");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("MC 百科页面过大.");
            output.Write(buffer, 0, count);
        }

        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    private static string? SourceSlug(string html, string platform, string prefix)
    {
        foreach (Match anchor in Regex.Matches(html, "<a\\b[^>]*href=[\"']([^\"']+)[\"'][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            if (!anchor.Groups[2].Value.Contains("common-linkicon-" + platform, StringComparison.OrdinalIgnoreCase)) continue;
            string link = WebUtility.HtmlDecode(anchor.Groups[1].Value);
            const string redirect = "//link.mcmod.cn/target/";
            int offset = link.IndexOf(redirect, StringComparison.OrdinalIgnoreCase);
            if (offset >= 0)
            {
                try
                {
                    string encoded = Uri.UnescapeDataString(link[(offset + redirect.Length)..]).Split('?', '#')[0].Replace('-', '+').Replace('_', '/');
                    link = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
                }
                catch (FormatException)
                {
                    continue;
                }
            }

            var match = Regex.Match(link, "^https?://(?:www\\.)?" + prefix + "([a-zA-Z0-9_-]+)(?:[/?#]|$)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
        }

        return null;
    }

    private static string Plain(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")).Trim();
}
