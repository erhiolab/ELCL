using System.Diagnostics.CodeAnalysis;

namespace ErhioLab.Craft.Launcher.Core.Downloads;

/// <summary>一个文件的目标路径及按优先顺序排列的下载地址.</summary>
public sealed record DownloadFileRequest(
    string DestinationPath,
    IReadOnlyList<Uri> Sources,
    long? ExpectedSize = null,
    string? ExpectedSha1 = null)
{
    /// <summary>调用方允许复用的本地候选路径. 只在提供预期 SHA-1 时尝试.</summary>
    public IReadOnlyList<string> ReusablePaths { get; init; } = [];
}

/// <summary>由调用方提交到下载队列的一组文件.</summary>
public sealed record DownloadTaskRequest(string Name, IReadOnlyList<DownloadFileRequest> Files);

/// <summary>单个任务的并发及总速度限制, 0 字节每秒表示不限速.</summary>
public sealed record DownloadOptions(int MaxConnections = 8, long SpeedLimitBytesPerSecond = 0)
{
    public const int DefaultMaxRetries = 2;
    public const int MaximumRetries = 10;
    public const int DefaultHttpTimeoutSeconds = 30;
    public const int MaximumHttpTimeoutSeconds = 600;

    /// <summary>单个来源在首次尝试之外最多重试多少次, 0 表示不重试.</summary>
    public int MaxRetries { get; init; } = DefaultMaxRetries;

    /// <summary>等待响应头或下一次网络读取的最长秒数.</summary>
    public int HttpTimeoutSeconds { get; init; } = DefaultHttpTimeoutSeconds;

    /// <summary>忽略文件请求中的本地复用候选, 直接从网络下载.</summary>
    public bool ForceRedownload { get; init; }

    internal TimeSpan HttpTimeout => TimeSpan.FromSeconds(HttpTimeoutSeconds);

    internal void Validate()
    {
        if (MaxConnections is < 1 or > 256 || SpeedLimitBytesPerSecond < 0 || MaxRetries is < 0 or > MaximumRetries
            || HttpTimeoutSeconds is < 1 or > MaximumHttpTimeoutSeconds)
            throw new ArgumentOutOfRangeException(nameof(DownloadOptions), "下载的连接数、限速、重试次数或 HTTP 超时超出范围.");
    }
}

/// <summary>失败或不支持分段的 HTTP 请求摘要, 不包含 URL 路径/查询或敏感标头.</summary>
public sealed record DownloadHttpDiagnostic(
    string Origin, string Phase, string Method, string? Range, int? StatusCode,
    IReadOnlyDictionary<string, string> ResponseHeaders, string? Detail);

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
[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global", Justification = "Progress data is part of the public library API.")]
public sealed record DownloadProgress(
    Guid TaskId,
    string TaskName,
    DownloadTaskStatus Status,
    int CompletedFiles,
    int TotalFiles,
    long BytesReceived,
    long? TotalBytes,
    string? CurrentFile)
{
    /// <summary>已通过校验并从本地复用的字节数, 不计入网络接收量.</summary>
    public long BytesReused { get; init; }
}

/// <summary>成功完成的任务结果.</summary>
[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global", Justification = "Download results are part of the public library API.")]
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
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global", Justification = "Callers can correlate the handle with download progress and results.")]
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
