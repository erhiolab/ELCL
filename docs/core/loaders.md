# 附加加载器版本

命名空间: `ErhioLab.Craft.Launcher.Core.Minecraft`

`MinecraftLoaderVersionClient` 按游戏版本查询 Forge、NeoForge、Fabric、Quilt、LiteLoader 和 OptiFine 的可用版本. 当前只查询版本信息, 不安装加载器.

## 获取版本

```csharp
using System;
using System.Net.Http;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient httpClient = new();
MinecraftLoaderVersionClient client = new(httpClient);
MinecraftLoaderVersionCatalog catalog = await client.GetVersionsAsync(
    MinecraftLoaderKind.Quilt, "1.21.1", MinecraftManifestSource.Official
);

foreach (MinecraftLoaderVersion version in catalog.Versions)
{
    Console.WriteLine(version.Version);
}
```

| API | 说明 |
| --- | --- |
| `new MinecraftLoaderVersionClient(httpClient, sourceTimeout)` | 调用方提供 `HttpClient`; 每个来源默认限时 8 秒. |
| `GetVersionsAsync(kind, gameVersion, preferredSource, cancellationToken)` | 查询指定游戏版本的加载器; 首选来源失败或结果为空时尝试备用来源. |
| `MinecraftLoaderCompatibility.CanCombine(gameVersion, firstKind, first, secondKind, second)` | 判断两个已取得的加载器版本是否属于当前已支持的组合. |

返回的 `MinecraftLoaderVersionCatalog` 包含 `Kind`、`GameVersion`、实际 `Source` 和 `Versions`. 至少一个来源成功解析但没有匹配版本时返回空列表; 两个来源都失败时抛出 `MinecraftLoaderVersionException`, 内部保留两次错误. 不支持的 Fabric 游戏版本可能由来源返回 400/404, Core 将其视为空列表. 调用方取消时传播取消异常.

## 版本字段

| `MinecraftLoaderVersion` 属性 | 含义 |
| --- | --- |
| `Version` | 加载器版本号. |
| `IsPreview` | 是否为预览或测试版本. |
| `ReleaseTime` / `ReleaseDate` | 来源明确提供的发布时间/日期; 可为空. |
| `LastModifiedDate` | 来源提供的更新时间, 不能当成发布时间. |
| `BuildDate` | 来源提供的构建日期. |
| `RequiredForgeVersion` | OptiFine 的 Forge 要求; `null` 表示不能搭配 Forge, 空字符串表示任意 Forge 版本. |

日期缺失是正常结果. 调用方不应根据版本号推测发布日期.

## 日期补充 API

版本查询会先返回列表. 需要额外日期时, 可单独调用以下方法, 再按版本号合并. 日期请求失败不影响已有版本列表.

| 方法 | 返回值与来源 |
| --- | --- |
| `GetForgeReleaseTimesAsync(gameVersion, versions, cancellationToken)` | Forge 官方下载页的发布时间, `IReadOnlyDictionary<string, DateTimeOffset>`. |
| `GetForgeVersionDatesAsync(gameVersion, cancellationToken)` | BMCLAPI Forge 镜像的 `modified` 更新时间, `IReadOnlyDictionary<string, DateOnly>`. |
| `GetFabricVersionDatesAsync(versions, cancellationToken)` | Fabric 官方 Maven 目录的更新日期, `IReadOnlyDictionary<string, DateOnly>`. |
| `GetQuiltVersionDatesAsync(versions, cancellationToken)` | Quilt 官方 Maven 构件目录的 `quilt-last-modified-timestamp` 更新日期, `IReadOnlyDictionary<string, DateOnly>`. 每次最多并发 6 个请求; 单个版本失败会略过. |

`versions` 是需要补日期的加载器版本号集合, 不是 Minecraft 游戏版本号. 对 Quilt 的大量版本, 调用方可分批查询以便逐批显示结果. 这些补充方法不依赖其他启动器维护的版本索引.

## 版本来源

| 加载器 | 官方来源 | 备用来源 |
| --- | --- | --- |
| Forge | [Forge Maven](https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml) | [BMCLAPI Forge](https://bmclapi2.bangbang93.com/forge/minecraft/1.21.1) |
| NeoForge | [NeoForge Maven](https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml) | [BMCLAPI NeoForge](https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge) |
| Fabric | [Fabric Meta](https://meta.fabricmc.net/v2/versions/loader/1.21.1) | [BMCLAPI Fabric Meta](https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.21.1) |
| Quilt | [Quilt Meta](https://meta.quiltmc.org/v3/versions/loader/1.21.1) | [BMCLAPI Quilt Meta](https://bmclapi2.bangbang93.com/quilt-meta/v3/versions/loader/1.21.1) |
| LiteLoader | [LiteLoader 元数据](https://dl.liteloader.com/versions/versions.json) | [BMCLAPI LiteLoader](https://bmclapi2.bangbang93.com/liteloader/list?mcversion=1.12.2) |
| OptiFine | [OptiFine 下载页](https://optifine.net/downloads) | [BMCLAPI OptiFine](https://bmclapi2.bangbang93.com/optifine/versionList) |

NeoForge `1.20.1` 使用旧版 Maven 坐标. Quilt 的备用接口在部分时间可能返回 404, Core 会尝试官方来源. 当前 NeoForge 官方列表没有逐版本日期, 因此相应日期字段可能为空.

组合规则目前仅允许经过验证的 Forge + OptiFine 搭配, 其中 Minecraft `1.13` 至 `1.14.3` 被排除; 其他加载器组合返回 `false`. `false` 表示当前 Core 没有确认支持该组合, 不代表上游永远不支持.
