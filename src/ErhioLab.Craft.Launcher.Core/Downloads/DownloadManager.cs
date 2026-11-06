using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace ErhioLab.Craft.Launcher.Core.Downloads;

/// <summary>按入队顺序执行下载任务, 并在单个任务内并发下载文件及分段.</summary>
public sealed class DownloadManager : IAsyncDisposable
{
    private const long MinimumSegmentSize = 2 * 1024 * 1024;
    private const int MaximumSegmentsPerFile = 4;
    private const int MaximumAttemptsPerSource = 3;
    private const int MaximumParallelFiles = 32;
    private readonly HttpClient _httpClient;

    private readonly Channel<QueuedTask> _queue = Channel.CreateUnbounded<QueuedTask>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );

    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private bool _disposed;

    /// <summary>使用调用方提供的 HTTP 客户端, 不接管其生命周期.</summary>
    public DownloadManager(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _worker = Task.Run(ProcessQueueAsync);
    }

    /// <summary>将一组文件加入队列, 返回可取消并等待结果的句柄.</summary>
    public DownloadTaskHandle Enqueue(
        DownloadTaskRequest request,
        DownloadOptions options,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        Validate(request);
        if (options.MaxConnections is < 1 or > 256 || options.SpeedLimitBytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "线程数须为 1-256, 速度限制不能为负数.");
        }
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 入队时复制调用方的列表, 避免排队期间修改来源或目标文件.
        request = new DownloadTaskRequest(request.Name, request.Files.Select(file => file with { Sources = file.Sources.ToArray() }).ToArray());
        Guid id = Guid.NewGuid();
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TaskCompletionSource<DownloadResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DownloadTaskHandle handle = new(id, completion.Task, cancellation);
        QueuedTask queued = new(id, request, options, progress, cancellation, completion);
        if (!_queue.Writer.TryWrite(queued))
        {
            cancellation.Dispose();
            throw new ObjectDisposedException(nameof(DownloadManager));
        }

        _ = completion.Task.ContinueWith(_ => cancellation.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        ReportSafely(progress, new DownloadProgress(id, request.Name, DownloadTaskStatus.Queued, 0, request.Files.Count, 0, null, null));
        return handle;
    }

    /// <summary>取消当前任务与排队任务, 等待文件句柄关闭.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Writer.TryComplete();
        _lifetime.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (QueuedTask queued in _queue.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetime.Token, queued.Cancellation.Token);
                DownloadState state = new(queued);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    state.Report(DownloadTaskStatus.Running, force: true);
                    using SemaphoreSlim connections = new(queued.Options.MaxConnections);
                    SpeedLimiter limiter = new(queued.Options.SpeedLimitBytesPerSecond);
                    await DownloadAllFilesAsync(queued, state, connections, limiter, linked.Token).ConfigureAwait(false);

                    state.Report(DownloadTaskStatus.Completed, force: true);
                    queued.Completion.TrySetResult(new DownloadResult(queued.Id,
                        queued.Request.Files.Select(file => Path.GetFullPath(file.DestinationPath)).ToArray()));
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    state.Report(DownloadTaskStatus.Canceled, force: true);
                    queued.Completion.TrySetCanceled();
                }
                catch (Exception error)
                {
                    state.Report(DownloadTaskStatus.Failed, force: true);
                    queued.Completion.TrySetException(error);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 关闭管理器时取消正在运行的任务.
        }
        finally
        {
            while (_queue.Reader.TryRead(out QueuedTask? remaining))
            {
                ReportSafely(remaining.Progress, new DownloadProgress(
                    remaining.Id, remaining.Request.Name, DownloadTaskStatus.Canceled,
                    0, remaining.Request.Files.Count, 0, null, null));
                remaining.Completion.TrySetCanceled();
            }
        }
    }

    private async Task DownloadAllFilesAsync(
        QueuedTask queued, DownloadState state, SemaphoreSlim connections, SpeedLimiter limiter, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(queued.Request.Files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(queued.Options.MaxConnections, MaximumParallelFiles),
                CancellationToken = cancellationToken
            }, async (file, token) =>
            {
                await DownloadFileAsync(file, queued.Options, state, connections, limiter, token).ConfigureAwait(false);
                state.CompleteFile(file.DestinationPath);
            }).ConfigureAwait(false);
    }

    private async Task DownloadFileAsync(
        DownloadFileRequest file, DownloadOptions options, DownloadState state,
        SemaphoreSlim connections, SpeedLimiter limiter, CancellationToken cancellationToken)
    {
        string destination = Path.GetFullPath(file.DestinationPath);
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        List<Exception> failures = [];
        bool lengthRegistered = false;

        foreach (Uri source in file.Sources)
        {
            for (int attempt = 0; attempt < MaximumAttemptsPerSource; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.part");
                AttemptCounter attemptBytes = new();
                try
                {
                    // 分段连接失败后改用普通 GET，避免持续触发同一站点的连接限制.
                    FileProbe probe = attempt == 0 && file.ExpectedSize is not <= MinimumSegmentSize
                        ? await ProbeWithConnectionAsync(source, connections, cancellationToken).ConfigureAwait(false)
                        : new FileProbe(file.ExpectedSize, false);
                    if (!lengthRegistered)
                    {
                        state.RegisterLength(probe.Length, file.ExpectedSize);
                        lengthRegistered = true;
                    }

                    if (file.ExpectedSize is { } expected && probe.Length is { } actual && actual != expected)
                    {
                        throw new InvalidDataException($"文件大小与预期不符: {source}");
                    }

                    try
                    {
                        await TransferAsync(source, temporary, probe, options.MaxConnections,
                            connections, limiter, state, file.DestinationPath,
                            attemptBytes.Add, cancellationToken).ConfigureAwait(false);
                    }
                    catch (RangeUnsupportedException)
                    {
                        state.RevertBytes(attemptBytes.Take());
                        File.Delete(temporary);
                        await TransferAsync(source, temporary, new FileProbe(null, false), 1,
                            connections, limiter, state, file.DestinationPath,
                            attemptBytes.Add, cancellationToken).ConfigureAwait(false);
                    }

                    if (file.ExpectedSize is { } requiredSize && new FileInfo(temporary).Length != requiredSize)
                    {
                        throw new InvalidDataException($"文件大小与预期不符: {source}");
                    }

                    if (file.ExpectedSha1 is { } expectedSha1)
                    {
                        await using FileStream input = File.OpenRead(temporary);
                        string actualSha1 = Convert.ToHexString(await SHA1.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
                        if (!actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException($"SHA-1 校验失败: {source}");
                        }
                    }

                    File.Move(temporary, destination, overwrite: true);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
                {
                    state.RevertBytes(attemptBytes.Take());
                    if (attempt + 1 < MaximumAttemptsPerSource && IsTransient(error))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(600 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    failures.Add(new IOException($"来源 {source} 下载失败.", error));
                    break;
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
        }

        throw new AggregateException($"所有来源都无法下载 {Path.GetFileName(destination)}.", failures);
    }

    private async Task<FileProbe> ProbeAsync(Uri source, CancellationToken token)
    {
        try
        {
            using HttpRequestMessage head = new(HttpMethod.Head, source);
            using HttpResponseMessage response = await _httpClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is > 0 and var length && response.Headers.AcceptRanges.Contains("bytes"))
            {
                return new FileProbe(length, true);
            }
        }
        catch (HttpRequestException)
        {
            // 某些站点不允许 HEAD, 继续尝试 GET 探测.
        }

        try
        {
            using HttpRequestMessage probe = new(HttpMethod.Get, source);
            probe.Headers.Range = new RangeHeaderValue(0, 0);
            using HttpResponseMessage ranged = await _httpClient.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!ranged.IsSuccessStatusCode)
            {
                return new FileProbe(null, false);
            }

            ContentRangeHeaderValue? range = ranged.Content.Headers.ContentRange;
            return ranged.StatusCode == HttpStatusCode.PartialContent && range is { From: 0, To: 0, Length: > 0 }
                ? new FileProbe(range.Length, true)
                : new FileProbe(ranged.StatusCode == HttpStatusCode.OK ? ranged.Content.Headers.ContentLength : null, false);
        }
        catch (HttpRequestException)
        {
            // 探测请求可能被拒绝; 普通 GET 仍可能成功.
            return new FileProbe(null, false);
        }
    }

    private async Task<FileProbe> ProbeWithConnectionAsync(Uri source, SemaphoreSlim connections, CancellationToken token)
    {
        await connections.WaitAsync(token).ConfigureAwait(false);
        try { return await ProbeAsync(source, token).ConfigureAwait(false); }
        finally { connections.Release(); }
    }

    private static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout } => true,
        HttpRequestException => false,
        OperationCanceledException => true,
        IOException => true,
        _ => false
    };

    private async Task TransferAsync(
        Uri source, string temporary, FileProbe probe, int maxConnections,
        SemaphoreSlim connections, SpeedLimiter limiter, DownloadState state,
        string filePath, Action<int> recordBytes, CancellationToken token)
    {
        await using FileStream output = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (probe.RangeSupported && probe.Length is > 0 and var length)
        {
            output.SetLength(length);
            int count = (int)Math.Min(Math.Min(maxConnections, MaximumSegmentsPerFile),
                Math.Max(1, (length + MinimumSegmentSize - 1) / MinimumSegmentSize));
            long segmentSize = (length + count - 1) / count;
            Task[] segments = Enumerable.Range(0, count).Select(index => TransferSegmentAsync(
                source, output.SafeFileHandle, index * segmentSize,
                Math.Min(length - 1, (index + 1L) * segmentSize - 1),
                length, connections, limiter, state, filePath, recordBytes, token)
            ).ToArray();
            await Task.WhenAll(segments).ConfigureAwait(false);
        }
        else
        {
            await connections.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, source);
                using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new InvalidDataException("服务器未返回完整文件.");
                }

                await using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await CopyAsync(input, output, null, limiter, state, filePath, recordBytes, token).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is { } expected && output.Length != expected)
                {
                    throw new InvalidDataException("下载长度与响应头不符.");
                }
            }
            finally
            {
                connections.Release();
            }
        }

        await output.FlushAsync(token).ConfigureAwait(false);
    }

    private async Task TransferSegmentAsync(
        Uri source, Microsoft.Win32.SafeHandles.SafeFileHandle output, long from, long to, long total,
        SemaphoreSlim connections, SpeedLimiter limiter, DownloadState state, string filePath,
        Action<int> recordBytes, CancellationToken token)
    {
        await connections.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, source);
            request.Headers.Range = new RangeHeaderValue(from, to);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                throw new RangeUnsupportedException();
            }

            response.EnsureSuccessStatusCode();
            ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent
                || range?.From != from || range.To != to || range.Length != total)
            {
                throw new InvalidDataException("服务器返回了不匹配的分段范围.");
            }

            await using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await CopyAsync(input, null, (output, from, to - from + 1), limiter, state, filePath, recordBytes, token).ConfigureAwait(false);
        }
        finally
        {
            connections.Release();
        }
    }

    private static async Task CopyAsync(
        Stream input, Stream? output,
        (Microsoft.Win32.SafeHandles.SafeFileHandle Handle, long Offset, long Length)? segment,
        SpeedLimiter limiter, DownloadState state, string filePath, Action<int> recordBytes, CancellationToken token)
    {
        byte[] buffer = new byte[limiter.BufferSize];
        long copied = 0;
        while (segment is null || copied < segment.Value.Length)
        {
            int wanted = segment is null ? buffer.Length : (int)Math.Min(buffer.Length, segment.Value.Length - copied);
            await limiter.WaitAsync(wanted, token).ConfigureAwait(false);
            int read = await input.ReadAsync(buffer.AsMemory(0, wanted), token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (segment is { } part)
            {
                await RandomAccess.WriteAsync(part.Handle, buffer.AsMemory(0, read), part.Offset + copied, token).ConfigureAwait(false);
            }
            else
            {
                await output!.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }

            copied += read;
            recordBytes(read);
            state.AddBytes(read, filePath);
        }

        if (segment is { } expectedSegment && copied != expectedSegment.Length)
        {
            throw new InvalidDataException("分段提前结束.");
        }
    }

    private static void Validate(DownloadTaskRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentNullException.ThrowIfNull(request.Files);
        if (request.Files.Count == 0)
        {
            throw new ArgumentException("文件列表不能为空.", nameof(request));
        }

        HashSet<string> destinations = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DownloadFileRequest file in request.Files)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(file.DestinationPath);
            ArgumentNullException.ThrowIfNull(file.Sources);
            if (!destinations.Add(Path.GetFullPath(file.DestinationPath)) ||
                file.Sources.Count == 0 ||
                file.Sources.Any(uri => uri is not { Scheme: "https" or "http" }) ||
                file.ExpectedSize is < 0 ||
                file.ExpectedSha1 is { } hash && (hash.Length != 40 || !hash.All(Uri.IsHexDigit))
               )
            {
                throw new ArgumentException("下载文件的目标路径/来源或校验参数无效.", nameof(request));
            }
        }
    }

    private static void ReportSafely(IProgress<DownloadProgress>? progress, DownloadProgress value)
    {
        try
        {
            progress?.Report(value);
        }
        catch (Exception)
        {
            // 观察者失败不应影响文件传输或使队列停止运行.
        }
    }

    private sealed record QueuedTask(
        Guid Id,
        DownloadTaskRequest Request,
        DownloadOptions Options,
        IProgress<DownloadProgress>? Progress,
        CancellationTokenSource Cancellation,
        TaskCompletionSource<DownloadResult> Completion);

    private sealed record FileProbe(long? Length, bool RangeSupported);

    private sealed class AttemptCounter
    {
        private long _bytes;

        internal void Add(int count) => Interlocked.Add(ref _bytes, count);

        internal long Take() => Interlocked.Exchange(ref _bytes, 0);
    }

    private sealed class RangeUnsupportedException : IOException;

    private sealed class DownloadState(QueuedTask task)
    {
        private readonly object _sync = new();
        private long _bytes;
        private long _totalBytes = task.Request.Files.Sum(file => file.ExpectedSize ?? 0);
        private int _unknownFiles = task.Request.Files.Count(file => file.ExpectedSize is null);
        private int _completedFiles;
        private long _lastReport;
        private string? _currentFile;

        internal void RegisterLength(long? length, long? expectedSize)
        {
            if (expectedSize.HasValue || !length.HasValue) return;
            lock (_sync)
            {
                _totalBytes += length.Value;
                _unknownFiles--;
            }
        }

        internal void AddBytes(int count, string file)
        {
            Interlocked.Add(ref _bytes, count);
            _currentFile = file;
            Report(DownloadTaskStatus.Running);
        }

        internal void RevertBytes(long count)
        {
            Interlocked.Add(ref _bytes, -count);
            Report(DownloadTaskStatus.Running, force: true);
        }

        internal void CompleteFile(string file)
        {
            Interlocked.Increment(ref _completedFiles);
            _currentFile = file;
            Report(DownloadTaskStatus.Running, force: true);
        }

        internal void Report(DownloadTaskStatus status, bool force = false)
        {
            if (task.Progress is null)
            {
                return;
            }

            DownloadProgress snapshot;
            lock (_sync)
            {
                long now = Environment.TickCount64;
                if (!force && now - _lastReport < 100)
                {
                    return;
                }

                _lastReport = now;
                snapshot = new DownloadProgress(task.Id, task.Request.Name, status,
                    _completedFiles, task.Request.Files.Count, Math.Max(0, Interlocked.Read(ref _bytes)),
                    _unknownFiles == 0 ? _totalBytes : null,
                    _currentFile);
            }

            ReportSafely(task.Progress, snapshot);
        }
    }

    private sealed class SpeedLimiter(long bytesPerSecond)
    {
        private readonly object _sync = new();
        private long _nextTick = Stopwatch.GetTimestamp();

        internal int BufferSize => bytesPerSecond == 0 ? 64 * 1024 : (int)Math.Min(64 * 1024, bytesPerSecond);

        internal async Task WaitAsync(int bytes, CancellationToken token)
        {
            if (bytesPerSecond == 0)
            {
                return;
            }

            long waitTicks;
            lock (_sync)
            {
                long now = Stopwatch.GetTimestamp();
                long start = Math.Max(now, _nextTick);
                waitTicks = start - now;
                long duration = (long)Math.Ceiling((double)bytes * Stopwatch.Frequency / bytesPerSecond);
                _nextTick = start + duration;
            }

            if (waitTicks > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds((double)waitTicks / Stopwatch.Frequency), token).ConfigureAwait(false);
            }
        }
    }
}
