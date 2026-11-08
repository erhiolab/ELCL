using System.ComponentModel;
using System.Diagnostics;
using System.IO;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Launching;

internal static class PreLaunchCommandRunner
{
    internal static async Task RunAsync(LauncherSettings settings, MinecraftLaunchPlaceholderContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.PreLaunchCommand)) return;
        cancellationToken.ThrowIfCancellationRequested();
        string command = MinecraftLaunchPlaceholders.Expand(settings.PreLaunchCommand, context);
        if (MinecraftLaunchPlaceholders.GetUnresolvedTokens(command).Count > 0) throw new InvalidOperationException(LocalizationManager.Get("gameLaunch.unknownCommand"));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
        {
            // /C 后必须保持用户命令串, 不能再次使用 ArgumentList 转义内部引号.
            Arguments = "/D /S /C \"" + command + "\"", WorkingDirectory = context.GameInstanceDirectory!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        };
        App.Logger.Information("Launch", $"开始执行启动前命令; 等待完成={settings.WaitForPreLaunchCommand}(不记录命令原文或输出).");
        var process = Process.Start(start) ?? throw new InvalidOperationException(LocalizationManager.Get("gameLaunch.commandFailed"));
        var completion = CompleteAsync(process);
        if (!settings.WaitForPreLaunchCommand)
        {
            _ = ObserveDetachedAsync(process, completion, cancellationToken);
            return;
        }

        try
        {
            int code = await completion.WaitAsync(cancellationToken);
            if (code != 0) throw new InvalidOperationException(string.Format(LocalizationManager.Get("gameLaunch.commandExit"), code));
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested) Kill(process);
            await completion;
            process.Dispose();
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        // 即使取消也继续排空输出, 由进程终止关闭管道, 防止命令被输出缓冲区卡住.
        while (await reader.ReadLineAsync(CancellationToken.None) is not null)
        {
        }
    }

    private static async Task<int> CompleteAsync(Process process)
    {
        await Task.WhenAll(DrainAsync(process.StandardOutput), DrainAsync(process.StandardError), process.WaitForExitAsync(CancellationToken.None));
        int code = process.ExitCode;
        App.Logger.Information("Launch", $"启动前命令已结束: 退出码={code}.");
        return code;
    }

    private static async Task ObserveDetachedAsync(Process process, Task<int> completion, CancellationToken cancellationToken)
    {
        var registration = cancellationToken.Register(static state => Kill((Process)state!), process);
        try
        {
            int code = await completion;
            if (code != 0) LauncherToasts.Error(string.Format(LocalizationManager.Get("gameLaunch.commandExit"), code));
        }
        catch (Exception error)
        {
            App.Logger.Error("Launch", "未等待的启动前命令发生错误.", error);
        }
        finally
        {
            registration.Dispose();
            process.Dispose();
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            App.Logger.Error("Launch", "取消启动时无法结束启动前命令.", error);
        }
    }
}
