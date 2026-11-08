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

`LocalMinecraftVersionScanner.Scan(gameDirectory)` 会查看指定目录下的 `versions` 文件夹，优先读取与目录同名的 JSON；没有同名文件时，尝试目录下的第一个 JSON。默认只返回能读取的版本。想记录缺失、损坏或无法访问的元数据，可传入 `onFileError` 回调，参数包含文件路径和原始异常。扫描不会改动游戏文件，也不会替你决定哪个目录是“默认”目录。

如果要做版本目录管理界面，可以传入 `includeInvalidVersions: true`，把无效目录也列出来：

```csharp
var entries = LocalMinecraftVersionScanner.Scan(gameDirectory,
    onFileError: (path, error) => Console.Error.WriteLine($"{path}: {error}"),
    includeInvalidVersions: true);

foreach (var entry in entries)
    Console.WriteLine(entry.IsValid ? entry.Id : $"{entry.Id}: {entry.Error}");
```

错误条目的 `Id` 使用目录名称，`FolderPath` 保留实际路径，`JsonPath` 是尝试读取的路径（可能不存在），其他版本字段为空。`Error` 区分 `MissingMetadata`、`InvalidMetadata` 和 `UnreadableMetadata`；正常条目为 `None`。正常版本按发布时间排序，错误目录排在其后。调用方可用这些信息显示错误图标、打开目录或提供删除操作，但不要把错误条目当成可启动版本，也不要登记为有效安装档案。`IsValid` 只表示这次元数据读取是否成功，不代表客户端 JAR、依赖及资源完整，启动前仍需检查。

返回的 `LocalMinecraftVersion` 除 `Id`、`Type` 和 `ReleaseTime` 外，还提供可为空的 `GameVersion`、`Loader`、`LoaderVersion`。扫描器优先读取 `clientVersion`、Forge/NeoForge 的 `arguments.game` 参数、`inheritsFrom`（必要时追溯本地父版本 JSON）以及 `libraries` 中的加载器坐标。仅当元数据不足时，才识别完整且明确的“游戏版本-加载器-加载器版本”命名格式；任意自定义名称中的数字不会被当作版本号。无法确认的字段保持为空，调用方可据此显示解析失败。

原版 JSON 的 `id` 和 `inheritsFrom` 除现代数字版本号、周快照外，也支持历史版本格式：例如 Pre-Classic 的 `rd-132211`、Classic 的 `c0.0.13a_03`、Indev/Infdev 的 `in-20100125` / `inf-20100618`、Alpha 的 `a1.2.6` 和 Beta 的 `b1.8.1`。这些 ID 会完整保留为 `GameVersion`，版本类别仍取自 JSON 的 `type`。只匹配完整格式，不从自定义名称中截取这类片段。

这个 API 提供的是**版本信息**，并不下载或安装游戏。
