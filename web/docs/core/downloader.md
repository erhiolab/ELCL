# 下载器

命名空间：`ErhioLab.Craft.Launcher.Core.Downloads`。

`DownloadManager` 按入队顺序处理任务。一个任务内可以并发下载多个文件，也可对支持 Range 的大文件分段。

## 创建 `DownloadManager` {#downloadmanager-constructor}

```csharp
using ErhioLab.Craft.Launcher.Core.Downloads;

using var http = new HttpClient();
await using var manager = new DownloadManager(http);
CancellationToken cancellationToken = default;
```

管理器不释放传入的 HTTP 客户端。应用退出时使用 `await using` 等待下载文件句柄关闭。

需要记录 HTTP 诊断时，改用带回调的构造方法：

```csharp
await using var tracedManager = new DownloadManager(http, value =>
{
    string headers = string.Join(", ", value.ResponseHeaders.Select(header => $"{header.Key}={header.Value}"));
    Console.WriteLine($"{value.Phase} {value.Method} {value.Origin} {value.StatusCode} {headers}");
});
```

传入 `null` 的 HTTP 客户端会抛出 `ArgumentNullException`。诊断回调可能在后台并发调用，抛出异常不会中断传输。它报告失败的 HTTP 请求及影响分段的异常响应，不会给每个成功请求生成记录。

| 诊断字段 | 含义 |
| --- | --- |
| `Origin` | 请求或重定向后响应的协议、主机与端口，不含用户信息、路径和查询参数。 |
| `Phase`、`Method`、`Range` | 请求阶段、HTTP 方法及分段范围；普通下载没有 `Range`。 |
| `StatusCode`、`Detail` | HTTP 状态码及可选的故障简述；未收到响应时可能没有状态码。 |
| `ResponseHeaders` | 筛选过的响应头，如 `Content-Range`、`Retry-After` 和 CDN 请求编号；不包含 Cookie、授权或重定向地址。 |

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

各文件共享连接预算；当前单任务最多并行 32 个文件，单个文件最多 4 段。单连接和已知不超过 2 MiB 的文件直接发送普通 GET。来源拒绝探测或不支持分段时，尝试用普通 GET 下载完整文件。

普通 GET 返回 403、404 等不可重试状态时，直接尝试下一来源。连接错误、429 或部分 5xx 状态有限重试；429 和 503 会遵守有效的 `Retry-After`，若要求等待超过 30 秒，本次跳过该来源。所有来源失败时抛出包含各来源错误的 `AggregateException`。

文件先写临时文件，校验后替换目标；取消或失败不会撤销已完成的其他文件。不会自动跳过已有文件，需要时先用[完整性检查](vanilla-installation.md#minecraftvanilladownloadplanner-missingfilesasync)。

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
