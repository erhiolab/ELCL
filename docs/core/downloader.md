# 通用下载器

`ErhioLab.Craft.Launcher.Core.Downloads.DownloadManager` 是独立于 ELCL 界面的 HTTP 文件下载队列. 调用方提供 `HttpClient`/目标路径/候选地址和下载参数. Core 不读取启动器配置, 也不决定文件保存位置.

## 基本用法

```csharp
using ErhioLab.Craft.Launcher.Core.Downloads;

using HttpClient http = new();
await using DownloadManager downloads = new(http);

DownloadTaskRequest task = new("示例任务", [
    new DownloadFileRequest(
        DestinationPath: "/path/to/example.jar",
        Sources: [
            new Uri("https://official.example/example.jar"),
            new Uri("https://mirror.example/example.jar")
        ],
        ExpectedSha1: "0123456789ABCDEF0123456789ABCDEF01234567")
]);

var progress = new Progress<DownloadProgress>(state =>
    Console.WriteLine($"{state.Status}: {state.BytesReceived}/{state.TotalBytes}")
);

DownloadTaskHandle handle = downloads.Enqueue(task, new DownloadOptions(MaxConnections: 8, SpeedLimitBytesPerSecond: 0), progress);
DownloadResult result = await handle.Completion;
```

示例地址和 SHA-1 是占位值. 实际使用时由调用方选择可信来源和校验值. `HttpClient` 的生命周期仍归调用方管理.

## 任务和来源

- `Enqueue` 按入队顺序执行任务. 一个任务可包含多个文件, 文件和分段可以并发下载; `MaxConnections` 限制该任务的同时连接数, 允许 `1` 到 `256`.
- 每个文件的 `Sources` 按顺序尝试. 首选地址失败/内容长度或 SHA-1 不符时, 清理本次临时文件并尝试下一个地址. 由调用方把官方源或镜像源放在前面.
- 服务器支持 HTTP Range 且已知长度时, 下载器分段写入同一临时文件. 不支持 Range 或不提供可靠长度时, 改用普通 GET. 分段过程中服务器不再遵守 Range 时, 也会退回普通 GET.
- 任务成功后, 临时文件才会移动到目标路径. 已有目标文件在下载失败或取消时保留; 成功时替换. 一个任务若包含多个文件, 已完成的文件不会因为后续文件失败而自动回滚.
- `ExpectedSize` 和 `ExpectedSha1` 是可选校验. 安装 Minecraft 时应尽量提供清单中的校验值. 所有候选地址都失败时, `Completion` 抛出异常; `Cancel()` 后抛出取消异常.

`DownloadProgress` 包含任务 ID/状态/完成文件数/已接收字节数/总字节数及当前文件. 长度未知时 `TotalBytes` 为 `null`; 切换来源时已接收字节数可能回退. 进度回调约每 100 毫秒更新一次, 状态切换时立即上报.

`SpeedLimitBytesPerSecond` 控制单个任务的总速率, `0` 表示不限制. 等待下载完成或调用 `Cancel()` 都不会阻塞 UI 线程. 管理器释放时会取消正在运行和排队的任务.

## 当前范围

此 API 下载调用方指定的文件, 不负责解析 Minecraft 版本 JSON/收集所需依赖/选择安装目录或执行安装. 后续游戏安装可以把一个版本所需的文件放进同一个 `DownloadTaskRequest`, 并为每个文件提供按优先级排列的官方与镜像地址.
