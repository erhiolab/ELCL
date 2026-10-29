using System.IO;
using System.Windows;

using ErhioLab.Craft.Launcher.Core.Logging;

namespace ErhioLab.Craft.Launcher;

/// <summary>启动器的 WPF 应用程序入口.</summary>
public partial class App : Application
{
    private static SessionFileLogger? _logger;

    /// <summary>供启动器界面记录操作的本次日志会话.</summary>
    internal static SessionFileLogger Logger =>
        _logger ?? throw new InvalidOperationException("日志系统尚未初始化.");

    protected override void OnStartup(StartupEventArgs e)
    {
        // 客户端决定数据目录, Core 仅接收日志目录并管理其中的文件.
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL");
        try
        {
            Directory.CreateDirectory(dataDirectory);
            _logger = new SessionFileLogger(Path.Combine(dataDirectory, "logs"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"无法初始化启动器数据目录:\n{exception.Message}", "ELCL 启动失败");
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += (_, args) => LogUnhandled("界面线程", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogUnhandled("应用程序", exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => LogUnhandled("后台任务", args.Exception);

        Logger.Information("App", $"启动器启动, 数据目录: {dataDirectory}");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _logger?.Information("App", $"启动器退出, 代码: {e.ApplicationExitCode}");
        }
        finally
        {
            _logger?.Dispose();
            _logger = null;
            base.OnExit(e);
        }
    }

    private static void LogUnhandled(string source, Exception exception)
    {
        try
        {
            _logger?.Error(source, "未处理的异常.", exception);
        }
        catch (Exception)
        {
            // 异常处理期间不能再因日志写入失败而掩盖原始异常.
        }
    }
}

