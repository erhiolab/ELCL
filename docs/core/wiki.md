# 中文 Minecraft Wiki 页面

命名空间: `ErhioLab.Craft.Launcher.Core.Minecraft`

`MinecraftWikiClient` 通过 MediaWiki API 获取 Java 版版本页面的纯文本. Wiki 是可选信息来源; 不参与 Mojang 版本清单查询.

```csharp
using System;
using System.Net.Http;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient httpClient = new();
MinecraftWikiClient wiki = new(httpClient);
MinecraftWikiPage? summary = await wiki.GetSummaryAsync("1.21.1");

if (summary is not null)
{
    Console.WriteLine(summary.Title);
    Console.WriteLine(summary.Text);
    Console.WriteLine(summary.Url);
}
```

| API | 用途 |
| --- | --- |
| `new MinecraftWikiClient(httpClient, apiUrl)` | 调用方提供 `HttpClient`; `apiUrl` 可选, 默认为中文 Minecraft Wiki API. |
| `GetSummaryAsync(versionId, cancellationToken)` | 取得页面开头的纯文本摘要. |
| `GetChangelogAsync(versionId, cancellationToken)` | 取得页面完整纯文本. |
| `MinecraftWikiClient.GetPageUrl(versionId)` | 构造浏览器可打开的版本页面地址, 不发网络请求. |

返回类型 `MinecraftWikiPage` 包含 `Title`、`Url` 和 `Text`. 页面不存在或没有可用文字时返回 `null`; HTTP 或响应格式错误会抛出异常. 两个查询方法均接受取消令牌. 页面来自 [中文 Minecraft Wiki](https://zh.minecraft.wiki/), 内容和可用性由该站点决定.
