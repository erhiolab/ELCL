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

`language` 为空或 `en` 时使用英文 Wiki；还支持 `zh`、`de`、`he`、`ja`、`lzh`、`pt`、`ru`。不支持的语言会抛出 `ArgumentOutOfRangeException`。需要替换 API 地址时仍可传入 `apiUrl`。页面地址与语言对应，查询结果的 `MinecraftWikiPage.Url` 也使用所选站点。

只想在浏览器打开 Wiki，可以用 `MinecraftWikiClient.GetPageUrl(versionId, language)` 构造地址，这一步不发网络请求。省略语言时使用英文 Wiki。
