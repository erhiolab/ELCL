# Minecraft Java 版版本清单

命名空间: `ErhioLab.Craft.Launcher.Core.Minecraft`

`MinecraftVersionCatalogClient` 获取 Java 版版本清单, 首选来源失败时尝试另一来源. 它只提供版本元数据, 不下载或安装游戏文件.

## 获取清单

```csharp
using System.Net.Http;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient httpClient = new();
MinecraftVersionCatalogClient client = new(httpClient);
MinecraftVersionCatalog catalog = await client.GetCatalogAsync(
    MinecraftManifestSource.Official
);
```

| API | 参数 | 返回/行为 |
| --- | --- | --- |
| `new MinecraftVersionCatalogClient(httpClient, officialUrl, mirrorUrl, sourceTimeout)` | 必填 `HttpClient`; 其余参数可选 | 默认使用 Mojang / BMCLAPI 地址, 单个来源默认超时 5 秒. 可传自定义 URL 与超时. |
| `GetCatalogAsync(preferredSource, cancellationToken)` | 首选来源与可选取消令牌 | 返回 `MinecraftVersionCatalog`; 首选失败时尝试另一来源. |
| `MinecraftVersionCatalogClient.Parse(json, source)` | 已有 JSON 文本及其来源 | 不发网络请求, 返回解析后的清单. |

`MinecraftManifestSource.Official` 和 `Mirror` 只表示首选顺序. `catalog.Source` 表示实际取得数据的来源. 两个来源都超时、请求失败或返回无效清单时抛出 `MinecraftVersionCatalogException`; `InnerException` 是包含两次原因的 `AggregateException`. 调用方取消时传播取消异常.

## 返回值

| 类型/属性 | 说明 |
| --- | --- |
| `MinecraftVersionCatalog.Source` | 实际来源. |
| `LatestReleaseId` / `LatestPreviewId` | 清单 `latest` 字段标记的最新正式版与预览版. |
| `Versions` | 按原清单顺序排列的 `MinecraftVersion` 集合. |
| `MinecraftVersion.Id` | 版本标识, 例如 `1.21.1`. |
| `Kind` | `Release`、`Preview`、`Ancient` 或 `AprilFools`. |
| `MetadataUrl` | 该版本元数据 JSON 的绝对 HTTPS 地址. |
| `ReleaseTime` | 带时区的发布时间. |
| `Sha1` | 元数据 SHA-1; 旧条目可能为 `null`. |

分类以 Mojang 清单的 `type` 为基础. 愚人节版本在清单中通常也是 `snapshot`, Core 根据已知版本 ID 与 4 月 1 日的发布时间归类. 新增特殊版本可能需要更新识别规则.

## 数据来源

- [Mojang 官方清单](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json)
- [BMCLAPI 镜像清单](https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json)

附加加载器版本由另一个 API 负责, 见[加载器](loaders.md).
