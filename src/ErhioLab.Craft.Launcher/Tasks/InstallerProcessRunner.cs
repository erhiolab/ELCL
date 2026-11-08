using System.Diagnostics;
using System.IO;

using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Tasks;

internal static class InstallerProcessRunner
{
    internal static async Task RunAsync(ProcessStartInfo start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process();
        process.StartInfo = start;
        if (!process.Start()) throw new InvalidOperationException(LocalizationManager.Get("install.processStartFailed"));
        App.Logger.Information("Installer", $"安装进程已创建: PID={process.Id}, Java={start.FileName}.");

        async Task DrainAsync(StreamReader reader)
        {
            int lines = 0;
            while (await reader.ReadLineAsync() is { } line)
            {
                if (++lines <= 10000) App.Logger.Information("Installer", line[..Math.Min(line.Length, 4096)]);
            }
        }

        Task output = DrainAsync(process.StandardOutput), error = DrainAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(token);
            await Task.WhenAll(output, error);
            if (process.ExitCode != 0) throw new InvalidOperationException(string.Format(LocalizationManager.Get("install.processExit"), process.ExitCode));
            App.Logger.Information("Installer", $"安装进程已完成: PID={process.Id}.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await Task.WhenAll(output, error);
        }
    }
}
