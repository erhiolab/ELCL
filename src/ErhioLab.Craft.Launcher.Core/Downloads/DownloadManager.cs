using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Channels;

using static ErhioLab.Craft.Launcher.Core.Downloads.DownloadHttpPolicy;

namespace ErhioLab.Craft.Launcher.Core.Downloads;

/// <summary>按入队顺序执行下载任务, 并在单个任务内并发下载文件及分段.</summary>
public sealed partial class DownloadManager : IAsyncDisposable
{
    private const long MinimumSegmentSize = 2 * 1024 * 1024;
    private const int MaximumSegmentsPerFile = 4;
    private const int MaximumParallelFiles = 32;
    private readonly HttpClient _httpClient;

    private readonly Channel<QueuedTask> _queue = Channel.CreateUnbounded<QueuedTask>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );

    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private bool _disposed;

    /// <summary>使用调用方提供的 HTTP 客户端, 不接管其生命周期.</summary>
    public DownloadManager(HttpClient httpClient) : this(httpClient, null) { }

    /// <summary>使用调用方的 HTTP 客户端, 可同时接收网络失败和分段回退的诊断.</summary>
    public DownloadManager(HttpClient httpClient, Action<DownloadHttpDiagnostic>? httpDiagnostic)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpDiagnostic = httpDiagnostic;
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
        options.Validate();
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 入队时复制调用方的列表, 避免排队期间修改来源或目标文件.
        request = new DownloadTaskRequest(request.Name, request.Files.Select(file => file with
        {
            Sources = file.Sources.ToArray(), ReusablePaths = file.ReusablePaths.ToArray()
        }).ToArray());
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
        if (!options.ForceRedownload && await TryReuseAsync(file, destination, cancellationToken).ConfigureAwait(false) is { } reused)
        {
            state.RegisterLength(reused, file.ExpectedSize);
            state.ReuseBytes(reused);
            return;
        }
        List<Exception> failures = [];
        bool lengthRegistered = false;

        foreach (Uri source in file.Sources)
        {
            for (int attempt = 0; attempt <= options.MaxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.part");
                AttemptCounter attemptBytes = new();
                try
                {
                    // 分段连接失败后改用普通 GET, 避免持续触发同一站点的连接限制.
                    FileProbe probe = attempt == 0 && options.MaxConnections > 1 && file.ExpectedSize is not <= MinimumSegmentSize
                        ? await ProbeWithConnectionAsync(source, connections, options.HttpTimeout, cancellationToken).ConfigureAwait(false)
                        : new FileProbe(file.ExpectedSize, false);
                    if (!lengthRegistered)
                    {
                        state.RegisterLength(probe.Length, file.ExpectedSize);
                        lengthRegistered = true;
                    }

                    if (file.ExpectedSize is { } expected && probe.Length is { } actual && actual != expected)
                    {
                        throw new InvalidDataException("文件大小与预期不符.");
                    }

                    try
                    {
                        await TransferAsync(source, temporary, probe, options.MaxConnections,
                            connections, limiter, state, file.DestinationPath,
                            attemptBytes.Add, options.HttpTimeout, cancellationToken).ConfigureAwait(false);
                    }
                    catch (RangeUnsupportedException)
                    {
                        state.RevertBytes(attemptBytes.Take());
                        File.Delete(temporary);
                        await TransferAsync(source, temporary, new FileProbe(null, false), 1,
                            connections, limiter, state, file.DestinationPath,
                            attemptBytes.Add, options.HttpTimeout, cancellationToken).ConfigureAwait(false);
                    }

                    if (file.ExpectedSize is { } requiredSize && new FileInfo(temporary).Length != requiredSize)
                    {
                        throw new InvalidDataException("文件大小与预期不符.");
                    }

                    if (file.ExpectedSha1 is { } expectedSha1)
                    {
                        await using FileStream input = File.OpenRead(temporary);
                        string actualSha1 = Convert.ToHexString(await SHA1.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
                        if (!actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("SHA-1 校验失败.");
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
                    if (attempt < options.MaxRetries && RetryDelay(error, attempt) is { } delay)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    failures.Add(new IOException($"来源 {Origin(source)} 下载失败: {error.Message}", error));
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

    private static async Task<long?> TryReuseAsync(DownloadFileRequest file, string destination, CancellationToken token)
    {
        if (file.ExpectedSha1 is not { } sha1) return null;
        foreach (string candidate in file.ReusablePaths)
        {
            token.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFullPath(candidate), destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            string temporary = destination + $".{Guid.NewGuid():N}.part";
            try
            {
                long length;
                await using (var input = File.OpenRead(candidate))
                {
                    if (file.ExpectedSize is { } size && input.Length != size) continue;
                    await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                    length = output.Length;
                    if (file.ExpectedSize is { } requiredSize && length != requiredSize) continue;
                    output.Position = 0;
                    string actual = Convert.ToHexString(await SHA1.HashDataAsync(output, token).ConfigureAwait(false));
                    if (!actual.Equals(sha1, StringComparison.OrdinalIgnoreCase)) continue;
                }

                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
                return length;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 候选失效时继续尝试或改为下载. */ }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 不因清理失败阻止备用来源. */ }
            }
        }
        return null;
    }

    private async Task<FileProbe> ProbeAsync(Uri source, TimeSpan timeout, CancellationToken token)
    {
        using (HttpRequestMessage head = new(HttpMethod.Head, source))
        using (HttpResponseMessage response = await SendAsync(head, "head", timeout, token).ConfigureAwait(false))
        {
            if (IsBackoffStatus(response.StatusCode)) throw HttpFailure(head, response);
            // 拒绝 HEAD 的来源可能允许普通 GET; 不再为同一文件尝试 Range 探测.
            if (!response.IsSuccessStatusCode) return new FileProbe(null, false);
            long? length = response.Content.Headers.ContentLength;
            if (response.Headers.AcceptRanges.Contains("none", StringComparer.OrdinalIgnoreCase) || length is <= MinimumSegmentSize)
                return new FileProbe(length, false);
            if (length is > 0 && response.Headers.AcceptRanges.Contains("bytes", StringComparer.OrdinalIgnoreCase))
                return new FileProbe(length, true);
        }

        using HttpRequestMessage probe = new(HttpMethod.Get, source);
        probe.Headers.Range = new RangeHeaderValue(0, 0);
        using HttpResponseMessage ranged = await SendAsync(probe, "range-probe", timeout, token).ConfigureAwait(false);
        if (IsBackoffStatus(ranged.StatusCode)) throw HttpFailure(probe, ranged);
        ContentRangeHeaderValue? range = ranged.Content.Headers.ContentRange;
        if (ranged.StatusCode == HttpStatusCode.PartialContent && range is { From: 0, To: 0, Length: > 0 } && IsByteRange(range))
            return new FileProbe(range.Length, range.Length > MinimumSegmentSize);
        if (ranged.IsSuccessStatusCode) ReportHttp(probe, "range-probe", ranged, "Range unavailable; using full GET.");
        return new FileProbe(ranged.StatusCode == HttpStatusCode.OK ? ranged.Content.Headers.ContentLength : null, false);
    }

    private async Task<FileProbe> ProbeWithConnectionAsync(Uri source, SemaphoreSlim connections, TimeSpan timeout, CancellationToken token)
    {
        await connections.WaitAsync(token).ConfigureAwait(false);
        try { return await ProbeAsync(source, timeout, token).ConfigureAwait(false); }
        finally { connections.Release(); }
    }

    private async Task TransferAsync(
        Uri source, string temporary, FileProbe probe, int maxConnections,
        SemaphoreSlim connections, SpeedLimiter limiter, DownloadState state,
        string filePath, Action<int> recordBytes, TimeSpan timeout, CancellationToken token)
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
                length, connections, limiter, state, filePath, recordBytes, timeout, token)
            ).ToArray();
            var transfer = Task.WhenAll(segments);
            try { await transfer.ConfigureAwait(false); }
            catch (Exception) when (!token.IsCancellationRequested && transfer.Exception is { } failure)
            {
                // 任何分段被限流时先遵守退避, 即使其他分段恰好返回不支持 Range.
                var backoff = failure.InnerExceptions.OfType<DownloadHttpException>()
                    .Where(error => error.StatusCode is { } status && IsBackoffStatus(status))
                    .MaxBy(error => error.RetryAfter ?? TimeSpan.Zero);
                if (backoff is not null) throw backoff;
                if (failure.InnerExceptions.Any(error => error is RangeUnsupportedException)) throw new RangeUnsupportedException();
                throw;
            }
        }
        else
        {
            await connections.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, source);
                using HttpResponseMessage response = await SendAsync(request, "body", timeout, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw HttpFailure(request, response);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    ReportHttp(request, "body", response, "Expected a complete file (HTTP 200).");
                    throw new InvalidDataException("服务器未返回完整文件.");
                }

                await CopyAsync(request, response, "body", output, null, limiter, state, filePath, recordBytes, timeout, token).ConfigureAwait(false);
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
        Action<int> recordBytes, TimeSpan timeout, CancellationToken token)
    {
        await connections.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, source);
            request.Headers.Range = new RangeHeaderValue(from, to);
            using HttpResponseMessage response = await SendAsync(request, "segment", timeout, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed or HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (response.IsSuccessStatusCode) ReportHttp(request, "segment", response, "Range ignored; using full GET.");
                throw new RangeUnsupportedException();
            }

            if (!response.IsSuccessStatusCode) throw HttpFailure(request, response);
            ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent
                || !IsByteRange(range) || range?.From != from || range.To != to || range.Length != total)
            {
                ReportHttp(request, "segment", response, "Invalid Content-Range; using full GET.");
                throw new RangeUnsupportedException();
            }

            await CopyAsync(request, response, "segment", null, (output, from, to - from + 1), limiter, state, filePath, recordBytes, timeout, token).ConfigureAwait(false);
        }
        finally
        {
            connections.Release();
        }
    }

    private async Task CopyAsync(
        HttpRequestMessage request, HttpResponseMessage response, string phase, Stream? output,
        (Microsoft.Win32.SafeHandles.SafeFileHandle Handle, long Offset, long Length)? segment,
        SpeedLimiter limiter, DownloadState state, string filePath, Action<int> recordBytes, TimeSpan timeout, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            await using Stream input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            deadline.CancelAfter(Timeout.InfiniteTimeSpan);
            await CopyBodyAsync(input, output, segment, limiter, state, filePath, recordBytes, deadline, timeout, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            const string detail = "HTTP response body read timed out.";
            ReportHttp(request, phase, response, detail);
            throw new HttpRequestException(HttpRequestError.Unknown, detail);
        }
    }

    private static async Task CopyBodyAsync(Stream input, Stream? output,
        (Microsoft.Win32.SafeHandles.SafeFileHandle Handle, long Offset, long Length)? segment,
        SpeedLimiter limiter, DownloadState state, string filePath, Action<int> recordBytes,
        CancellationTokenSource deadline, TimeSpan timeout, CancellationToken token)
    {
        byte[] buffer = new byte[limiter.BufferSize];
        long copied = 0;
        while (segment is null || copied < segment.Value.Length)
        {
            int wanted = segment is null ? buffer.Length : (int)Math.Min(buffer.Length, segment.Value.Length - copied);
            await limiter.WaitAsync(wanted, token).ConfigureAwait(false);
            deadline.CancelAfter(timeout);
            int read = await input.ReadAsync(buffer.AsMemory(0, wanted), deadline.Token).ConfigureAwait(false);
            deadline.CancelAfter(Timeout.InfiniteTimeSpan);
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
            ArgumentNullException.ThrowIfNull(file.ReusablePaths);
            if (!destinations.Add(Path.GetFullPath(file.DestinationPath)) ||
                file.Sources.Count == 0 ||
                file.Sources.Any(uri => uri is not { Scheme: "https" or "http" }) ||
                file.ReusablePaths.Any(string.IsNullOrWhiteSpace) || file.ExpectedSize is < 0 ||
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
        private long _reusedBytes;
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

        internal void ReuseBytes(long count) => Interlocked.Add(ref _reusedBytes, count);

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
                    _currentFile) { BytesReused = Interlocked.Read(ref _reusedBytes) };
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
