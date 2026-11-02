namespace ErhioLab.Craft.Launcher.Core.Downloads;

/// <summary>一个文件的目标路径及按优先顺序排列的下载地址.</summary>
public sealed record DownloadFileRequest(
    string DestinationPath,
    IReadOnlyList<Uri> Sources,
    long? ExpectedSize = null,
    string? ExpectedSha1 = null);

/// <summary>由调用方提交到下载队列的一组文件.</summary>
public sealed record DownloadTaskRequest(string Name, IReadOnlyList<DownloadFileRequest> Files);

/// <summary>单个任务的并发及总速度限制, 0 字节每秒表示不限速.</summary>
public sealed record DownloadOptions(int MaxConnections = 8, long SpeedLimitBytesPerSecond = 0);

/// <summary>下载任务在队列中的状态.</summary>
public enum DownloadTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled
}

/// <summary>下载状态及累计进度; 总大小未知时 TotalBytes 为 null.</summary>
public sealed record DownloadProgress(
    Guid TaskId,
    string TaskName,
    DownloadTaskStatus Status,
    int CompletedFiles,
    int TotalFiles,
    long BytesReceived,
    long? TotalBytes,
    string? CurrentFile);

/// <summary>成功完成的任务结果.</summary>
public sealed record DownloadResult(Guid TaskId, IReadOnlyList<string> SavedFiles);

/// <summary>入队后可等待或取消的任务句柄.</summary>
public sealed class DownloadTaskHandle
{
    private readonly CancellationTokenSource _cancellation;

    internal DownloadTaskHandle(Guid id, Task<DownloadResult> completion, CancellationTokenSource cancellation)
    {
        Id = id;
        Completion = completion;
        _cancellation = cancellation;
    }

    /// <summary>本次任务的唯一标识.</summary>
    public Guid Id { get; }

    /// <summary>任务结束后返回保存路径; 失败或取消时抛出相应异常.</summary>
    public Task<DownloadResult> Completion { get; }

    /// <summary>请求取消排队中或运行中的任务.</summary>
    public void Cancel()
    {
        if (Completion.IsCompleted)
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException) when (Completion.IsCompleted)
        {
            // 恰好在任务结束时取消, 无需再处理.
        }
    }
}
