# 下载文件

`DownloadManager` 管理下载队列。一个任务可以有多个文件；每个文件可以按优先顺序列出几个地址。首选地址失败时，下载器会换下一个。

```csharp
using ErhioLab.Craft.Launcher.Core.Downloads;

using HttpClient http = new();
await using DownloadManager downloads = new(http);

var file = new DownloadFileRequest(
    DestinationPath: @"D:\Downloads\example.jar",
    Sources: [
        new Uri("https://example.org/example.jar"),
        new Uri("https://mirror.example.org/example.jar")
    ]);

var task = new DownloadTaskRequest("示例下载", [file]);
var progress = new Progress<DownloadProgress>(state =>
    Console.WriteLine($"{state.Status} · {state.BytesReceived} 字节"));

var handle = downloads.Enqueue(
    task,
    new DownloadOptions(MaxConnections: 8, SpeedLimitBytesPerSecond: 0),
    progress);

var result = await handle.Completion;
```

示例地址需要换成真实地址。把官方源或镜像源放在 `Sources` 前面，就能决定尝试顺序。`MaxConnections` 可设为 1–256；限速填 `0` 表示不限速。

下载器会同时处理多个文件，最多并行 32 个文件，实际连接数还受 `MaxConnections` 限制。大文件在服务器支持时最多使用 4 个分段连接；已知大小且不超过 2 MiB 的文件直接下载，省去额外的探测请求。连接中断或服务器返回 429 等临时错误时，每个来源最多尝试 3 次，重试改用普通请求；404 等非临时错误会直接尝试下一来源。下载先写临时文件，成功后才替换目标文件。可以为文件提供预期大小或 SHA-1 做校验；所有地址都失败时，`Completion` 抛出异常。调用 `handle.Cancel()` 可以取消任务。

进度包含已完成文件数、当前文件、已收到字节数和总字节数。请求中全部提供预期大小时，开始下载即可得到总字节数；部分长度未知且无法探测时，总字节数为 `null`。切换来源时，已收到字节数可能回退。多个文件中已完成的文件不会因为后续文件失败而自动回滚。

`DownloadManager` 接收**已经确定的文件地址**，不会替调用方决定游戏目录或安装顺序。若要安装原版 Minecraft，可用 `MinecraftVanillaDownloadPlanner` 从版本 JSON 和资产索引生成这些地址：

1. 用版本清单中的 `MinecraftVersion` 构造 `VersionMetadata` 下载请求，并用下载器取得版本 JSON。
2. 调用 `ParseVersion`，得到客户端 JAR、资产索引和支持库的请求。版本 JAR 属于 `versions/<名称>/`；支持库存放在游戏根目录的 `libraries/`，供多个版本共用。如果版本 JSON 提供客户端日志配置文件，计划的 `LoggingConfiguration` 也会有一个请求，存放于 `assets/log_configs/`。
3. 下载资产索引，调用 `ParseAssets` 得到 `assets/objects/<哈希前两位>/<完整哈希>` 下的资源请求。索引位于 `assets/indexes/`，也供版本共享。
4. 每批下载前可用 `MissingFilesAsync` 检查已有文件的大小和 SHA-1，只提交缺失或损坏的文件。全部成功后调用 `WriteInstalledVersionAsync`，最后写入版本目录里的 JSON。自定义版本名称会写入该 JSON 的 `id`。

这几个方法只解析元数据并接收调用方给出的路径、来源顺序和取消令牌；任务队列、界面状态以及文件下载源设置由调用方管理。支持库规则按当前操作系统筛选。镜像地址根据 BMCLAPI 的路径规则生成，不支持映射的来源只使用原地址。
