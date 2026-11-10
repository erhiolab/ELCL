# 下载器

命名空间：`ErhioLab.Craft.Launcher.Core.Downloads`。

`DownloadManager` 按入队顺序处理任务。一个任务内可以并发下载多个文件，也可对支持 Range 的大文件分段。

## 创建 `DownloadManager`

```csharp
using ErhioLab.Craft.Launcher.Core.Downloads;

using var http = new HttpClient();
await using var manager = new DownloadManager(http);
CancellationToken cancellationToken = default;
```

管理器不释放传入的 HTTP 客户端。应用退出时使用 `await using` 等待下载文件句柄关闭。

## `DownloadManager.Enqueue` {#downloadmanager-enqueue}

提交一个或一批文件，立即返回 `DownloadTaskHandle`；等待 `Completion` 才知道最终结果。

```csharp
var request = new DownloadTaskRequest("示例下载", new[]
{
    new DownloadFileRequest(
        Path.GetFullPath("downloads/example.zip"),
        new[] { new Uri("https://example.com/example.zip") })
});
var progress = new Progress<DownloadProgress>(value =>
    Console.WriteLine($"{value.Status}：{value.BytesReceived}/{value.TotalBytes}"));

var handle = manager.Enqueue(request,
    new DownloadOptions(MaxConnections: 8, SpeedLimitBytesPerSecond: 0),
    progress, cancellationToken);

DownloadResult result = await handle.Completion;
```

示例地址需换成真实文件直链。

| 输入 | 含义 |
| --- | --- |
| `DownloadTaskRequest.Name`、`Files` | 任务名与文件集合。目标路径不能重复。 |
| `DownloadFileRequest.DestinationPath`、`Sources` | 目标文件和按优先级排列的候选 URL。 |
| `ExpectedSize`、`ExpectedSha1` | 可选的长度与 SHA-1，完成后用于校验。 |
| `DownloadOptions.MaxConnections` | 单个任务共享的连接上限，1～256，默认 8。 |
| `SpeedLimitBytesPerSecond` | 整个任务的字节／秒速率上限，0 不限制。 |
| `progress`、`cancellationToken` | 可选，状态回调和取消令牌。 |

各文件共享连接预算；当前单任务最多并行 32 个文件，单个文件最多 4 段，每段至少约 2 MiB。不支持分段时使用普通下载。

每个来源有有限重试，全部失败再抛出包含来源错误的异常。文件先写临时文件，校验后替换目标；取消或失败不会撤销已完成的其他文件。不会自动跳过已有文件，需要时先用[完整性检查](vanilla-installation.md#minecraftvanilladownloadplanner-missingfilesasync)。

## `DownloadTaskHandle.Cancel` {#downloadtaskhandle-cancel}

```csharp
handle.Cancel();
```

请求取消排队或进行中的任务。任务已结束时没有效果。应用仍应观察 `Completion`，把 `OperationCanceledException` 和下载失败分开处理。

## `DownloadManager.DisposeAsync` {#downloadmanager-disposeasync}

```csharp
await manager.DisposeAsync();
```

取消当前与排队任务，并等待工作线程和文件句柄结束。释放后不能再入队，重复释放可以安全返回。通常由 `await using` 自动调用，无需像示例这样再次显式调用。

## 理解进度

| 字段 | 含义 |
| --- | --- |
| `TaskId`、`TaskName`、`Status` | 任务身份与排队、运行、完成、失败、取消状态。 |
| `CompletedFiles`、`TotalFiles` | 已完成与总文件数。 |
| `BytesReceived`、`TotalBytes` | 本次累计字节与总大小；长度无法确定时总大小为空。 |
| `CurrentFile` | 最近报告进度的文件，不代表只下载这一项。 |

切换来源或重试时字节值可能回退；不要把它当成永久递增的已安装容量。实时速度可由相邻采样的字节差与时间差计算，核心没有速度字段。

## 网络边界

下载器不替你登录网站或解析网盘页面。需要 Cookie、权限或防盗链的网站可能返回 403。通用下载器不会自行放开 HTTPS 降级限制，重定向策略还受传入的 HTTP 处理器影响。

要安装 Minecraft，应先用[原版计划](vanilla-installation.md)或[加载器计划](loader-installation.md)确定文件，再提交队列。
