using System.Net.Http;
using System.Net.Http.Json;

using ErhioLab.Craft.Launcher.Core.Logging;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Analytics;

/// <summary>统计事件的统一入口及发送实现. 业务代码仅调用具名方法, 不依赖服务商协议.</summary>
internal sealed class AnalyticsEvents : IDisposable
{
    private readonly Lock _sync = new();
    private readonly Func<string> _getInstallationId;
    private readonly HttpClient _httpClient;
    private readonly SessionFileLogger _logger;
    private readonly bool _ownsHttpClient;
    private readonly HashSet<CancellationTokenSource> _pending = [];
    private bool _enabled;
    private bool _startupProcessed;
    private volatile bool _disposed;

    internal AnalyticsEvents(Func<string> getInstallationId, SessionFileLogger logger, HttpClient? httpClient = null)
    {
        _getInstallationId = getInstallationId;
        _logger = logger;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    internal void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _enabled = enabled;
            if (!enabled)
                foreach (var request in _pending.ToArray())
                    request.Cancel();
        }
    }

    /// <summary>每个启动器进程只尝试一次 launcher_opened.</summary>
    internal Task LauncherOpenedAsync()
    {
        lock (_sync)
        {
            if (_disposed || _startupProcessed) return Task.CompletedTask;
            _startupProcessed = true;
            return CaptureLocked("launcher_opened");
        }
    }

    /// <summary>由启动流程在游戏窗口首次出现时调用, 每次成功启动各记录一次.</summary>
    internal Task GameLaunchSucceededAsync()
    {
        lock (_sync) return CaptureLocked("game_launch_succeeded");
    }

    // 调用者持有 _sync. 不重试、不补报, 不在此处采集账户或游戏详情.
    private Task CaptureLocked(string eventName)
    {
        if (_disposed || !_enabled) return Task.CompletedTask;
        var cancellation = new CancellationTokenSource();
        _pending.Add(cancellation);
        var timestamp = DateTimeOffset.UtcNow;
        return Task.Run(() => SendAsync(eventName, timestamp, cancellation), CancellationToken.None);
    }

    private async Task SendAsync(string eventName, DateTimeOffset timestamp, CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            var id = _getInstallationId();
            token.ThrowIfCancellationRequested();
            Log($"开始发送匿名事件 {eventName}.");
            var payload = new
            {
                api_key = ServiceConfiguration.PostHogProjectToken,
                @event = eventName,
                distinct_id = id,
                timestamp,
                properties = new Dictionary<string, object?>
                {
                    ["$process_person_profile"] = false,
                    ["$is_identified"] = false,
                    ["$geoip_disable"] = true,
                    ["$ip"] = null
                }
            };
            using var response = await _httpClient.PostAsJsonAsync(ServiceConfiguration.PostHogCaptureEndpoint, payload, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            Log($"匿名事件 {eventName} 已发送.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Log($"匿名事件 {eventName} 已取消.");
        }
        catch (Exception error)
        {
            // 统计失败不得阻断启动, 不记录请求正文/安装标识或机器码.
            if (!_disposed)
            {
                try
                {
                    _logger.Error("Analytics", $"匿名事件 {eventName} 发送失败, 本次不重试.", error);
                }
                catch (Exception loggingError) when (loggingError is ObjectDisposedException or System.IO.IOException)
                {
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _pending.Remove(cancellation);
                cancellation.Dispose();
            }
        }
    }

    private void Log(string message)
    {
        if (_disposed) return;
        try
        {
            _logger.Information("Analytics", message);
        }
        catch (Exception error) when (error is ObjectDisposedException or System.IO.IOException)
        {
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var request in _pending.ToArray()) request.Cancel();
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }
}
