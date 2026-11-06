# 游戏版本列表

`MinecraftVersionCatalogClient` 负责取得 Minecraft Java 版版本清单。它会先尝试你指定的来源；失败、超时或返回无效清单时，再尝试另一个来源。

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

每个来源默认最多等待 5 秒，可通过构造函数的 `sourceTimeout` 调整。如果一轮中两个来源都失败，并且至少一个是暂时性的连接错误、超时、HTTP 408、429 或 5xx，Core 会等 400 毫秒，再按相同优先顺序重试一轮。这样首次连接时的 TLS 握手中断等短暂故障，不必让用户手动刷新。清单格式错误和其他 4xx 错误本身不构成重试理由；调用方主动取消也会立即停止。

两轮仍失败时会抛出 `MinecraftVersionCatalogException`，内部保留各次请求的错误，便于查日志。Core 不保存版本列表缓存；如果想在离线时仍显示上次获取的版本，需要由调用方自行保存清单或解析后的结果。如果你已经拿到了清单 JSON，也可以用 `MinecraftVersionCatalogClient.Parse(json, source)` 离线解析。愚人节分类依据已知版本 ID 和发布时间，新版本可能需要更新识别规则。

## 扫描已有游戏目录

`LocalMinecraftVersionScanner.Scan(gameDirectory)` 会查看指定目录下的 `versions` 文件夹，读取每个版本的 JSON。损坏的单个文件会跳过；想记录原因，可传入 `onFileError` 回调。扫描不会改动游戏文件，也不会替你决定哪个目录是“默认”目录。

返回的 `LocalMinecraftVersion` 除 `Id`、`Type` 和 `ReleaseTime` 外，还提供可为空的 `GameVersion`、`Loader`、`LoaderVersion`。扫描器优先读取 `clientVersion`、Forge/NeoForge 的 `arguments.game` 参数、`inheritsFrom`（必要时追溯本地父版本 JSON）以及 `libraries` 中的加载器坐标。仅当元数据不足时，才识别完整且明确的“游戏版本-加载器-加载器版本”命名格式；任意自定义名称中的数字不会被当作版本号。无法确认的字段保持为空，调用方可据此显示解析失败。

这个 API 提供的是**版本信息**，并不下载或安装游戏。
