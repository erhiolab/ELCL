using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

using ErhioLab.Craft.Launcher.Core.Logging;
using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Styles;

namespace ErhioLab.Craft.Launcher;

/// <summary>启动器的 WPF 应用程序入口.</summary>
public partial class App : Application
{
    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<LauncherTheme>() }
    };

    private static SessionFileLogger? _logger;
    private static JsonFileStore<LauncherSettings>? _settingsStore;

    /// <summary>当前已加载并应用的设置.</summary>
    internal static LauncherSettings Settings { get; private set; } = new();

    /// <summary>供启动器界面记录操作的本次日志会话.</summary>
    internal static SessionFileLogger Logger =>
        _logger ?? throw new InvalidOperationException("日志系统尚未初始化.");

    protected override void OnStartup(StartupEventArgs e)
    {
        // 客户端决定数据目录, Core 仅接收日志目录并管理其中的文件.
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL");
        string settingsPath = Path.Combine(dataDirectory, "config.json");
        try
        {
            Directory.CreateDirectory(dataDirectory);
            _logger = new SessionFileLogger(Path.Combine(dataDirectory, "logs"));
            _settingsStore = new JsonFileStore<LauncherSettings>(settingsPath, SettingsJsonOptions);
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

        try
        {
            Settings = _settingsStore!.Load(new LauncherSettings()).Normalize();
            if (!File.Exists(settingsPath))
            {
                _settingsStore.Save(Settings);
            }

            Logger.Information("Settings",
                $"配置已加载, 主题: {Settings.Theme}, 主题色: {Settings.AccentColor}, 不透明度: {Settings.WindowOpacity:P0}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 保留无法解析的配置文件, 只在本次运行中使用默认值.
            Logger.Error("Settings", "读取配置失败, 本次使用默认设置.", exception);
            Settings = new LauncherSettings();
        }

        LauncherThemeManager.Apply(Settings);
        Logger.Information("App", $"启动器启动, 数据目录: {dataDirectory}");
        base.OnStartup(e);
    }

    /// <summary>保存并立即应用新设置.</summary>
    /// <param name="settings">页面提交的新设置.</param>
    /// <returns>保存成功时为 true.</returns>
    internal static bool UpdateSettings(LauncherSettings settings)
    {
        LauncherSettings normalized = settings.Normalize();
        try
        {
            _settingsStore!.Save(normalized);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Error("Settings", "保存配置失败.", exception);
            MessageBox.Show($"无法保存外观设置:\n{exception.Message}", "ELCL 设置");
            return false;
        }

        bool paletteChanged = Settings.Theme != normalized.Theme
                              || !string.Equals(Settings.AccentColor, normalized.AccentColor,
                                  StringComparison.OrdinalIgnoreCase);
        Settings = normalized;
        if (paletteChanged)
        {
            LauncherThemeManager.Apply(Settings);
        }

        if (Current.MainWindow is MainWindow window)
        {
            window.ApplyContentOpacity(Settings.WindowOpacity);
        }

        Logger.Information("Settings",
            $"外观设置已更新, 主题: {Settings.Theme}, 主题色: {Settings.AccentColor}, 不透明度: {Settings.WindowOpacity:P0}");
        return true;
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
