# 游戏版本列表

`MinecraftVersionCatalogClient` 负责取得 Minecraft Java 版版本清单。它会按你指定的顺序尝试官方源和 BMCLAPI 镜像。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient http = new();
var client = new MinecraftVersionCatalogClient(http);
var catalog = await client.GetCatalogAsync(MinecraftManifestSource.Official);

Console.WriteLine($"最新正式版：{catalog.LatestReleaseId}");
Console.WriteLine($"最新预览版：{catalog.LatestPreviewId}");
Console.WriteLine($"实际来源：{catalog.Source}");
```

`catalog.Versions` 按原清单顺序保存每个版本。`Id` 是版本号，`Kind` 区分正式版、预览版、远古版与愚人节版；`ReleaseTime` 是发布时间，`MetadataUrl` 指向该版本的元数据 JSON，`Sha1` 可能为空。

两个来源都失败时会抛出 `MinecraftVersionCatalogException`，内部保留两次错误。调用方主动取消时抛出取消异常。如果你已经拿到了清单 JSON，也可以用 `MinecraftVersionCatalogClient.Parse(json, source)` 离线解析。愚人节分类依据已知版本 ID 和发布时间，新版本可能需要更新识别规则。

## 扫描已有游戏目录

`LocalMinecraftVersionScanner.Scan(gameDirectory)` 会查看指定目录下的 `versions` 文件夹，读取每个版本的 JSON。损坏的单个文件会跳过；想记录原因，可传入 `onFileError` 回调。扫描不会改动游戏文件，也不会替你决定哪个目录是“默认”目录。

这个 API 提供的是**版本信息**，并不下载或安装游戏。
