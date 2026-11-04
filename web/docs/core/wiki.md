# 读取 Minecraft Wiki

如果你想在版本列表旁显示更新摘要，可以用 `MinecraftWikiClient` 读取中文 Minecraft Wiki 的版本页面。它和 Mojang 版本清单是两条独立的请求。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient http = new();
var wiki = new MinecraftWikiClient(http);
var page = await wiki.GetSummaryAsync("1.21.1");

if (page is not null)
    Console.WriteLine(page.Text);
```

`GetSummaryAsync` 返回开头的纯文本；`GetChangelogAsync` 读取完整纯文本。页面不存在或没有可用文字时返回 `null`。网络或响应出错会抛出异常，适合当作可选信息处理，不要让它挡住版本列表。

只想在浏览器打开 Wiki，可以用 `MinecraftWikiClient.GetPageUrl(versionId)` 构造地址，这一步不发网络请求。
