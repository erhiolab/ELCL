using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Launching;

/// <summary>持续排空游戏输出, 日志仅保存脱敏后的内容, 直到进程退出.</summary>
internal sealed class RunningGame : IDisposable
{
    private readonly StreamWriter _log;
    private readonly string[] _secrets;
    private readonly object _logGate = new();
    internal Process Process { get; }
    internal string LogPath { get; }
    internal Task<int> Completion { get; }

    private RunningGame(Process process, StreamWriter log, string path, string[] secrets)
    {
        Process = process;
        _log = log;
        LogPath = path;
        _secrets = secrets;
        Completion = ReadUntilExitAsync();
    }

    internal static RunningGame Start(ProcessStartInfo start, string logPath, string token, string? refreshToken, GameProcessPriority priority)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("无法创建 Java 进程.");
        }
        catch
        {
            log.Dispose();
            throw;
        }

        try
        {
            process.PriorityClass = priority switch
            {
                GameProcessPriority.High => ProcessPriorityClass.AboveNormal,
                GameProcessPriority.Low => ProcessPriorityClass.BelowNormal,
                _ => ProcessPriorityClass.Normal
            };
            App.Logger.Information("Launch", $"游戏进程优先级已应用: {priority}.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("Launch", "无法设置游戏进程优先级, 将继续启动.", error);
        }

        return new(process, log, logPath, new[] { token, refreshToken }.Where(value => value is { Length: > 1 }).Cast<string>().ToArray());
    }

    private async Task<int> ReadUntilExitAsync()
    {
        async Task Drain(StreamReader reader, string channel)
        {
            try
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    foreach (string secret in _secrets) line = line.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                    lock (_logGate) _log.WriteLine($"{DateTimeOffset.Now:O} [{channel}] {line}");
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                try
                {
                    App.Logger.Warning("Launch", $"读取游戏输出失败: {error.GetType().Name}.");
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        await Task.WhenAll(Drain(Process.StandardOutput, "OUT"), Drain(Process.StandardError, "ERR"), Process.WaitForExitAsync()).ConfigureAwait(false);
        return Process.ExitCode;
    }

    internal void Kill()
    {
        try
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            App.Logger.Error("Launch", "取消启动时无法结束游戏进程.", error);
        }
    }

    internal async Task TerminateAsync()
    {
        // 只操作这次启动持有的 Process, 避免 PID 复用或误伤其他 Java 进程.
        if (!Completion.IsCompleted)
        {
            try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (Completion.IsCompleted || Process.HasExited) { }
        }
        await Completion;
    }

    public void Dispose()
    {
        _log.Dispose();
        Process.Dispose();
    }
}
