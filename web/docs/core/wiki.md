# Minecraft Wiki

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

查询某个 Java 版版本的摘要、正文和实际语言页面。Wiki 请求独立于游戏清单，适合做可选说明，不要让读取失败阻止游戏下载。

## 创建 `MinecraftWikiClient`

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
var wiki = new MinecraftWikiClient(http, language: "zh");
CancellationToken cancellationToken = default;
```

`language` 是优先语言，省略或空值使用英文。支持格式有效的语言代码，例如 `zh`、`ja`、`de`、`lzh`，没有固定白名单；无效格式抛出 `ArgumentOutOfRangeException`。

`apiUrl` 可替换用于发现页面的英文 Wiki API，翻译页面仍使用跨语言链接对应站点的 API。

## `MinecraftWikiClient.GetSummaryAsync` {#minecraftwikiclient-getsummaryasync}

取得页面开头的纯文本摘要。

```csharp
MinecraftWikiPage? summary = await wiki.GetSummaryAsync("1.21.1", cancellationToken);
if (summary is not null) Console.WriteLine(summary.Text);
```

参数为实际游戏 ID。返回标题、实际 URL 与文本，页面不存在或没有可用文字时返回 `null`。

## `MinecraftWikiClient.GetChangelogAsync` {#minecraftwikiclient-getchangelogasync}

取得完整页面纯文本，而不是只截取开头。

```csharp
MinecraftWikiPage? page = await wiki.GetChangelogAsync("1.21.1", cancellationToken);
if (page is not null) Console.WriteLine(page.Url);
```

完整正文可能较长，如何分段展示由界面层处理。它不是结构化的新增／修复项目列表。

## `MinecraftWikiClient.GetLanguagesAsync` {#minecraftwikiclient-getlanguagesasync}

查询这个版本页面实际提供的语言链接。

```csharp
var languages = await wiki.GetLanguagesAsync("1.21.1", cancellationToken);
foreach (var language in languages)
    Console.WriteLine($"{language.Language}：{language.Title} · {language.Url}");
```

返回 `MinecraftWikiLanguage` 集合，包含英文页面本身。英文页面不存在时返回空列表。

这是逐页面的语言列表，不是 Wiki 全站语言清单，也不需要自行猜测翻译页面标题。

## `MinecraftWikiClient.GetPageUrlAsync` {#minecraftwikiclient-getpageurlasync}

```csharp
Uri? url = await wiki.GetPageUrlAsync("1.21.1", cancellationToken);
```

解析真正存在的页面规范地址，遵循与正文相同的语言回退规则。英文页面也不存在时返回 `null`。

## `MinecraftWikiClient.GetPageUrl`（已弃用） {#minecraftwikiclient-getpageurl}

静态方法只拼接候选地址，不能确认翻译是否存在。新代码请使用 `GetPageUrlAsync`；旧代码迁移时需要等待网络查询，而不是只改方法名。

## 语言回退与失败

优先语言在跨语言链接中不存在，或翻译页面不存在时，回退到英文。空文字本身不触发英文回退；最后结果仍可能是 `null`。

网络故障、无效 API 响应和主动取消会正常抛出异常，不会伪装成“没有翻译”。核心不持久化缓存，调用方可以按需保存结果。
