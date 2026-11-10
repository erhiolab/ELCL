# 游戏版本列表

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

这里查询 Mojang 发布的版本，不扫描本地安装，也不自动下载游戏。已有游戏目录请看[本地版本扫描](local-versions.md)。

## 创建 `MinecraftVersionCatalogClient`

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
var client = new MinecraftVersionCatalogClient(
    http, sourceTimeout: TimeSpan.FromSeconds(5));
CancellationToken cancellationToken = default;
```

`httpClient` 由调用方管理；`officialUrl` 和 `mirrorUrl` 可以替换清单端点。`sourceTimeout` 是单个来源的等待时间，默认 5 秒，必须大于零。

## `MinecraftVersionCatalogClient.GetCatalogAsync` {#minecraftversioncatalogclient-getcatalogasync}

按优先来源取得清单，返回 `MinecraftVersionCatalog`。

```csharp
var catalog = await client.GetCatalogAsync(
    MinecraftManifestSource.Official, cancellationToken);
Console.WriteLine(catalog.LatestReleaseId);
Console.WriteLine(catalog.LatestPreviewId);
Console.WriteLine(catalog.Source);
```

| 参数 | 含义 |
| --- | --- |
| `preferredSource` | `Official` 官方优先，或 `Mirror` 镜像优先。 |
| `cancellationToken` | 可选，取消请求与重试等待。 |

首选来源失败、超时或清单无效时会尝试另一来源。如果两个都失败，且存在暂时性网络故障，会等 400 毫秒再试一轮。HTTP 408、429、5xx 等可以触发这次重试，明确的格式错误和普通 4xx 本身不会。

最终失败抛出 `MinecraftVersionCatalogException`，内部保留各次异常；主动取消不会包装为来源故障。

## `MinecraftVersionCatalogClient.Parse` {#minecraftversioncatalogclient-parse}

解析已取得的 JSON，不进行网络请求，也不保存缓存。

```csharp
string json = await File.ReadAllTextAsync("manifest.json", cancellationToken);
var cached = MinecraftVersionCatalogClient.Parse(
    json, MinecraftManifestSource.Official);
Console.WriteLine(cached.Versions.Count);
```

`source` 用于标明这份 JSON 的来源，不会据此访问网络。内容无效时抛出 JSON 相关异常。

想离线展示上次结果时，可由应用保存原始清单，下次用本方法读取，再后台刷新。

## 返回字段

| 字段 | 含义 |
| --- | --- |
| `Source` | 本次实际返回结果的来源。 |
| `LatestReleaseId`、`LatestPreviewId` | 官方标记的最新正式版与预览版。 |
| `Versions` | 按清单顺序排列的版本集合。 |
| `MinecraftVersion.Id` | 原始游戏 ID，后续查询或下载用它。 |
| `Kind` | `Release`、`Preview`、`Ancient` 或 `AprilFools`。 |
| `MetadataUrl`、`Sha1` | 版本 JSON 地址及可为空的校验值。 |
| `ReleaseTime` | 发布时间。 |

愚人节分类依赖已知 ID 与日期规则，新发布版本可能需要更新核心识别规则。

## 本地版本与目录管理

原来在本页的本地方法已分篇：

- [扫描已有游戏目录](local-versions.md)。
- [重命名、查引用和删除版本](version-directories.md)。
