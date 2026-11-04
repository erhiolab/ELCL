# 模组加载器版本

给定游戏版本后，`MinecraftLoaderVersionClient` 可以查询 Forge、NeoForge、Fabric、Quilt、LiteLoader 和 OptiFine 的版本列表。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient http = new();
var client = new MinecraftLoaderVersionClient(http);
var result = await client.GetVersionsAsync(
    MinecraftLoaderKind.Quilt, "1.21.1", MinecraftManifestSource.Official);

foreach (var loader in result.Versions)
    Console.WriteLine(loader.Version);
```

`result.Source` 是实际使用的来源。首选来源失败或没有结果时会尝试备用来源；两个来源都失败时抛出 `MinecraftLoaderVersionException`。如果来源正常、但这个游戏版本确实没有对应加载器，返回的是空列表。比如旧版本没有 Fabric，不必把它当成程序错误。

有些版本带 `ReleaseTime`，有些只有更新日期，也有些没有日期。**不要把更新时间当成发布时间。** Core 还提供 Forge、Fabric、Quilt 的额外日期查询方法；它们需要再访问上游页面或 Maven 目录，查询失败不影响已经拿到的版本列表。

`MinecraftLoaderCompatibility.CanCombine(...)` 用来检查当前已确认的加载器组合。目前只确认部分 Forge + OptiFine 组合；返回 `false` 意味着 Core 暂未确认支持，不代表社区里绝对没有可行方法。

这里查询的是**可用版本**，不执行加载器安装。
