using System.Net;

namespace ErhioLab.Craft.Launcher.Core.Downloads;

/// <summary>为不进入文件队列的小型 HTTP 下载提供同一套重试和超时规则.</summary>
public static class DownloadHttpPolicy
{
    /// <summary>执行一个来源的下载. 单次小文件操作的总时限由选项决定, 取消不重试.</summary>
    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, DownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        for (int attempt = 0;; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Exception failure;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(options.HttpTimeout);
                try
                {
                    return await operation(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = new HttpRequestException("HTTP request timed out.");
                }
                catch (Exception error) when (error is IOException or HttpRequestException)
                {
                    failure = error;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (attempt < options.MaxRetries && RetryDelay(failure, attempt) is { } delay)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            else
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        }
    }

    internal static bool IsBackoffStatus(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    internal static DownloadHttpException HttpFailure(HttpRequestMessage request, HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        TimeSpan? delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return new DownloadHttpException($"{request.Method} HTTP {(int)response.StatusCode} ({response.StatusCode}).", response.StatusCode, delay);
    }

    internal static TimeSpan? RetryDelay(Exception error, int attempt)
    {
        if (!IsTransient(error)) return null;
        TimeSpan fallback = TimeSpan.FromMilliseconds((error is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ? 1000 : 600) * (attempt + 1));
        if (error is DownloadHttpException { StatusCode: { } status, RetryAfter: { } hint } && IsBackoffStatus(status))
        {
            // 过长的等待交给下一来源, 而不是在 Retry-After 到期前重试.
            if (hint > TimeSpan.FromSeconds(30)) return null;
            if (hint > fallback) return hint;
        }

        return fallback;
    }

    private static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException
        {
            StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
        } => true,
        IOException or OperationCanceledException => true,
        _ => false
    };
}

internal sealed class DownloadHttpException(string message, HttpStatusCode status, TimeSpan? retryAfter) : HttpRequestException(message, null, status)
{
    internal TimeSpan? RetryAfter { get; } = retryAfter;
}
