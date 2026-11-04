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

如果服务器支持分段请求，下载器会并发下载；不支持时自动改用普通请求。下载先写临时文件，成功后才替换目标文件。可以为文件提供预期大小或 SHA-1 做校验；所有地址都失败时，`Completion` 抛出异常。调用 `handle.Cancel()` 可以取消任务。

进度包含已完成文件数、当前文件、已收到字节数和总字节数。总长度未知时可能为 `null`；切换来源时，已收到字节数可能回退。多个文件中已完成的文件不会因为后续文件失败而自动回滚。

这个下载器接收**已经确定的文件地址**。它目前不会自己解析 Minecraft 版本依赖，也不会自动完成游戏安装。
