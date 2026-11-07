# 读取 Minecraft Wiki

如果你想在版本列表旁显示更新摘要，可以用 `MinecraftWikiClient` 读取 Minecraft Wiki 的版本页面。它和 Mojang 版本清单是两条独立的请求。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient http = new();
var wiki = new MinecraftWikiClient(http, language: "zh");
var page = await wiki.GetSummaryAsync("1.21.1");

if (page is not null)
    Console.WriteLine(page.Text);
```

`GetSummaryAsync` 返回开头的纯文本；`GetChangelogAsync` 读取完整纯文本。页面不存在或没有可用文字时返回 `null`。网络或响应出错会抛出异常，适合当作可选信息处理，不要让它挡住版本列表。

`language` 表示优先语言。空值或 `en` 使用英文；其他语言代码不再由固定白名单决定，例如可传 `zh`、`de`、`he`、`ja`、`lzh`、`pt`、`ru`、`es`、`fr`、`it`、`uk`。格式无效的代码会抛出 `ArgumentOutOfRangeException`。某个版本是否提供该语言，由该版本英文页面的 MediaWiki `langlinks` 返回值决定；语言链接携带实际页面标题和 URL，因此不需要猜测各语言的标题格式。

优先语言没有出现在跨语言链接中，或链接指向的页面已经不存在时，摘要、完整正文和页面地址都回退到该版本的英文页面。英文页面也不存在时，返回 `null`。空摘要不会被当成页面不存在；网络错误、API 响应无效和取消操作会正常抛出，不会伪装成“没有翻译”。`MinecraftWikiPage.Url` 始终对应返回文字的实际页面。

```csharp
// 每个版本实际提供的语言可能不同，列表包含英文页面本身。
IReadOnlyList<MinecraftWikiLanguage> languages = await wiki.GetLanguagesAsync("26.3");
foreach (var language in languages)
    Console.WriteLine($"{language.Language}: {language.Title} — {language.Url}");

// 与摘要使用相同的语言选择和回退规则。
Uri? pageUrl = await wiki.GetPageUrlAsync("26.3");
```

`GetLanguagesAsync` 返回的是当前页面的跨语言链接，不是整个 Wiki 站点固定的语言清单。英文页面不存在时返回空列表。读取时设置 `lllimit=max`，避免默认只返回前十条链接。Core 不持久化缓存这些信息。

构造函数的 `apiUrl` 可替换用于发现页面的**英文 Wiki API** 地址；后续翻译请求使用跨语言链接对应站点的 `/api.php`。API 会跟随 Wiki 页面重定向，使用返回的 `fullurl` 作为规范地址。

旧的静态 `MinecraftWikiClient.GetPageUrl(versionId, language)` 只会拼接未经验证的候选地址，已标记过时。请迁移到实例方法 `GetPageUrlAsync`；只有网络查询才能判断当前版本是否提供所选语言。
