# 模组加载器版本

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

支持查询 Forge、NeoForge、Fabric、Quilt、LiteLoader 和 OptiFine。查询结果不代表已经安装，安装流程见[安装模组加载器](loader-installation.md)。

## 创建 `MinecraftLoaderVersionClient`

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
var client = new MinecraftLoaderVersionClient(http,
    sourceTimeout: TimeSpan.FromSeconds(8));
CancellationToken cancellationToken = default;
```

默认每个来源等待 8 秒，`sourceTimeout` 必须大于零。HTTP 客户端由调用方管理。

## `MinecraftLoaderVersionClient.GetVersionsAsync` {#minecraftloaderversionclient-getversionsasync}

```csharp
var quilt = await client.GetVersionsAsync(
    MinecraftLoaderKind.Quilt, "1.21.1",
    MinecraftManifestSource.Official, cancellationToken);
foreach (var version in quilt.Versions)
    Console.WriteLine(version.Version);
```

| 参数 | 含义 |
| --- | --- |
| `kind` | 要查询的加载器枚举值。 |
| `gameVersion` | 实际 Minecraft ID，不是用户自定义安装名。 |
| `preferredSource` | 官方优先或镜像优先。 |
| `cancellationToken` | 可选，控制取消。 |

首选来源失败或返回空列表时，尝试另一来源。至少有一个来源正常但没有构建时，允许返回空列表；两者均失败才抛出 `MinecraftLoaderVersionException`。

`catalog.Source` 是实际来源，`Versions` 为可选版本。旧游戏版本没有对应加载器属于正常结果。

## `MinecraftLoaderVersionClient.GetForgeReleaseTimesAsync` {#minecraftloaderversionclient-getforgereleasetimesasync}

解析 Forge 官方下载页面，补充指定版本的精确发布时间。

```csharp
var forge = await client.GetVersionsAsync(MinecraftLoaderKind.Forge,
    "1.20.1", MinecraftManifestSource.Official, cancellationToken);
var releaseTimes = await client.GetForgeReleaseTimesAsync("1.20.1",
    forge.Versions.Take(10).Select(value => value.Version).ToArray(), cancellationToken);
```

返回“加载器版本 → `DateTimeOffset`”字典。没有解析到的版本不出现在字典中；网络与页面变化可能使查询失败，不应阻止原有列表显示。

## `MinecraftLoaderVersionClient.GetForgeVersionDatesAsync` {#minecraftloaderversionclient-getforgeversiondatesasync}

从镜像补充逐版本的更新时间，不是官方发布时间。

```csharp
var forgeDates = await client.GetForgeVersionDatesAsync("1.20.1", cancellationToken);
```

只返回存在 `LastModifiedDate` 的条目。即使列表查询选了官方优先，此方法仍会访问镜像；不希望增加镜像依赖时，可以不调用。

## `MinecraftLoaderVersionClient.GetFabricVersionDatesAsync` {#minecraftloaderversionclient-getfabricversiondatesasync}

从 Fabric 官方 Maven 目录读取构件目录的更新日期。

```csharp
var fabric = await client.GetVersionsAsync(MinecraftLoaderKind.Fabric,
    "1.21.1", MinecraftManifestSource.Official, cancellationToken);
var fabricDates = await client.GetFabricVersionDatesAsync(
    fabric.Versions.Select(value => value.Version).ToArray(), cancellationToken);
```

返回 `DateOnly` 字典，没找到的日期保持缺省。它不提供精确到时分秒的发布时间。

## `MinecraftLoaderVersionClient.GetQuiltVersionDatesAsync` {#minecraftloaderversionclient-getquiltversiondatesasync}

读取 Quilt 官方 Maven 构件目录的更新时间。

```csharp
var quiltDates = await client.GetQuiltVersionDatesAsync(
    quilt.Versions.Select(value => value.Version).ToArray(), cancellationToken);
```

逐版本请求，最多 6 个并发；成功结果在当前进程中缓存。单个版本失败会跳过该项，不会让其余版本全部失败，主动取消仍会停止查询。

## `MinecraftLoaderCompatibility.CanCombine` {#minecraftloadercompatibility-cancombine}

判断两项选择是否属于核心明确支持的组合。

```csharp
var forgeChoice = new MinecraftLoaderVersion("47.2.0", false);
var optiFineChoice = new MinecraftLoaderVersion("HD_U_I6", false,
    RequiredForgeVersion: "47.2.0");
bool compatible = MinecraftLoaderCompatibility.CanCombine("1.20.1",
    MinecraftLoaderKind.Forge, forgeChoice,
    MinecraftLoaderKind.OptiFine, optiFineChoice);
```

版本对象最好直接使用查询结果，示例仅演示参数形状。

当前只确认元数据明确兼容的 Forge＋OptiFine，同类或其他组合返回 `false`。OptiFine 的 `RequiredForgeVersion` 为 `null` 时不支持组合，为空字符串时表示未限制具体 Forge，非空时按完整版本或构建号匹配；部分历史 Minecraft 范围会额外拒绝组合。

`false` 表示核心没有确认支持，不意味着社区一定不存在其他安装办法。

## 日期字段不要混用

| 字段 | 含义 |
| --- | --- |
| `ReleaseTime` | 精确发布时间，可能为空。 |
| `ReleaseDate` | 来源明确给出的发布日期。 |
| `LastModifiedDate` | 最后更新时间，不能改称发布日期。 |
| `BuildDate` | 构建日期。 |
| `IsPreview` | 是否预览或测试构建。 |

这些日期不保证每个加载器都有。展示时可明确写“更新于”或留空，不要自行补出一个发布时间。
