using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace ErhioLab.Craft.Launcher.Core.Downloads;

public sealed partial class DownloadManager
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private readonly Action<DownloadHttpDiagnostic>? _httpDiagnostic;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string phase, CancellationToken token)
    {
        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) ReportHttp(request, phase, response, null);
            return response;
        }
        catch (HttpRequestException error)
        {
            var current = error.InnerException;
            while (current is not null and not SocketException) current = current.InnerException;
            string detail = current is SocketException socket
                ? $"{error.HttpRequestError}; SocketException ({socket.NativeErrorCode}, {socket.SocketErrorCode})"
                : error.HttpRequestError.ToString();
            ReportHttp(request, phase, null, detail, error.StatusCode);
            // 第三方处理器的异常文本可能包含签名 URL, 只保留结构化错误信息.
            throw new HttpRequestException(error.HttpRequestError, detail, null, error.StatusCode);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            const string detail = "HTTP request timed out.";
            ReportHttp(request, phase, null, detail);
            throw new HttpRequestException(HttpRequestError.Unknown, detail);
        }
    }

    private void ReportHttp(HttpRequestMessage request, string phase, HttpResponseMessage? response, string? detail,
        HttpStatusCode? status = null)
    {
        if (_httpDiagnostic is not { } report) return;

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (response is not null)
        {
            if (response.Content.Headers.ContentLength is { } length) headers["Content-Length"] = length.ToString(CultureInfo.InvariantCulture);
            if (response.Content.Headers.ContentRange is { } range && IsByteRange(range)) headers["Content-Range"] = range.ToString();
            if (response.Headers.AcceptRanges.Contains("bytes", StringComparer.OrdinalIgnoreCase)) headers["Accept-Ranges"] = "bytes";
            else if (response.Headers.AcceptRanges.Contains("none", StringComparer.OrdinalIgnoreCase)) headers["Accept-Ranges"] = "none";
            if (response.Headers.RetryAfter is { } retry) headers["Retry-After"] = retry.ToString();
            if (response.Headers.TryGetValues("CF-Ray", out var values) && values.FirstOrDefault() is { Length: > 0 and <= 40 } ray && ray.All(value => char.IsAsciiHexDigit(value) || value == '-' || char.IsAsciiLetterUpper(value))) headers["CF-Ray"] = ray;
        }

        var address = response?.RequestMessage?.RequestUri is { IsAbsoluteUri: true } effectiveUri ? effectiveUri : request.RequestUri!;
        var diagnostic = new DownloadHttpDiagnostic(Origin(address), phase, request.Method.Method, request.Headers.Range?.ToString(),
            (int?)(response?.StatusCode ?? status), headers, detail);
        try
        {
            report(diagnostic);
        }
        catch (Exception)
        {
            /* 调用方的诊断处理不影响下载. */
        }
    }

    private static string Origin(Uri source) => source.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);

    private static bool IsByteRange(ContentRangeHeaderValue? range) => string.Equals(range?.Unit, "bytes", StringComparison.OrdinalIgnoreCase);

    private static bool IsBackoffStatus(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static DownloadHttpException HttpFailure(HttpRequestMessage request, HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        TimeSpan? delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return new DownloadHttpException($"{request.Method} HTTP {(int)response.StatusCode} ({response.StatusCode}).", response.StatusCode, delay);
    }

    private static TimeSpan? RetryDelay(Exception error, int attempt)
    {
        if (!IsTransient(error)) return null;
        TimeSpan fallback = TimeSpan.FromMilliseconds((error is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ? 1000 : 600) * (attempt + 1));
        if (error is DownloadHttpException { StatusCode: { } status, RetryAfter: { } hint } && IsBackoffStatus(status))
        {
            // 过长的等待交给下一来源, 而不是在 Retry-After 到期前重试.
            if (hint > MaxRetryDelay) return null;
            if (hint > fallback) return hint;
        }

        return fallback;
    }

    private sealed class DownloadHttpException(string message, HttpStatusCode status, TimeSpan? retryAfter) : HttpRequestException(message, null, status)
    {
        internal TimeSpan? RetryAfter { get; } = retryAfter;
    }
}
