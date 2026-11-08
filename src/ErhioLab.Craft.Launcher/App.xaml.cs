using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Logging;
using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Accounts;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Styles;
using ErhioLab.Craft.Launcher.Tasks;
using ErhioLab.Craft.Launcher.Launching;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher;

/// <summary>启动器的 WPF 应用程序入口.</summary>
public partial class App
{
    static App()
    {
        // 全局使用屏幕像素排版; 同时覆盖独立 Popup/ToolTip 和文档文本,
        // 避免只设置主窗口时, 弹出层仍使用 Ideal 模式并在换行处拉宽空格.
        TextOptions.TextFormattingModeProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(TextFormattingMode.Display, FrameworkPropertyMetadataOptions.Inherits));
        TextOptions.TextFormattingModeProperty.OverrideMetadata(typeof(FrameworkContentElement),
            new FrameworkPropertyMetadata(TextFormattingMode.Display, FrameworkPropertyMetadataOptions.Inherits));
    }

    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter<LauncherTheme>(), new JsonStringEnumConverter<TitleBarMode>(),
            new JsonStringEnumConverter<VersionListSource>(), new JsonStringEnumConverter<GameProcessPriority>(),
            new JsonStringEnumConverter<GameWindowMode>(), new JsonStringEnumConverter<GameGarbageCollector>()
        }
    };

    private static SessionFileLogger? _logger;
    private static JsonFileStore<LauncherSettings>? _settingsStore;
    private static LauncherAccountStore? _accountStore;
    private static LauncherSkinService? _skinService;
    private static ServiceCredentials? _serviceCredentials;
    private static readonly HttpClient DownloadHttpClient = new();
    private static readonly DownloadManager DownloadEngine = new(DownloadHttpClient);
    private static readonly LauncherTaskManager TaskEngine = new();
    private static readonly JavaCatalogManager JavaEngine = new();
    internal static GameLaunchService GameLauncher { get; } = new();
    internal static MemoryOptimizationService MemoryOptimizer { get; } = new();

    /// <summary>当前已加载并应用的设置.</summary>
    internal static LauncherSettings Settings { get; private set; } = new();

    /// <summary>供启动器界面记录操作的本次日志会话.</summary>
    internal static SessionFileLogger Logger => _logger ?? throw new InvalidOperationException("日志系统尚未初始化.");

    /// <summary>所有启动器页面共享的下载任务队列.</summary>
    internal static DownloadManager Downloads => DownloadEngine;

    /// <summary>所有页面共享的启动器任务管理器.</summary>
    internal static LauncherTaskManager Tasks => TaskEngine;

    /// <summary>本机 Java 的发现/排序与持久化管理.</summary>
    internal static JavaCatalogManager JavaCatalog => JavaEngine;

    internal static LauncherAccountStore Accounts => _accountStore ?? throw new InvalidOperationException("账户尚未初始化.");
    internal static LauncherSkinService Skins => _skinService ?? throw new InvalidOperationException("皮肤缓存尚未初始化.");
    internal static ServiceCredentials Services => _serviceCredentials ?? throw new InvalidOperationException("服务配置尚未初始化.");

    protected override void OnStartup(StartupEventArgs e)
    {
        // 客户端决定数据目录, Core 仅接收日志目录并管理其中的文件.
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL");
        string settingsPath = Path.Combine(dataDirectory, "config.json");
        string localGameDirectory = Path.Combine(AppContext.BaseDirectory, ".minecraft");
        try
        {
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(localGameDirectory);
            _logger = new SessionFileLogger(Path.Combine(dataDirectory, "logs"));
            _settingsStore = new JsonFileStore<LauncherSettings>(settingsPath, SettingsJsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LocalizationManager.Apply(Settings.Language);
            LauncherThemeManager.Apply(Settings);
            LauncherDialogs.Error(LocalizationManager.Get("app.startFailed"), string.Format(LocalizationManager.Get("app.dataDirectoryFailed"), exception.Message));
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
            Settings = _settingsStore.Load(new LauncherSettings()).Normalize();
            LocalizationManager.Apply(Settings.Language);
            if (Settings.GameFolders.Count == 0)
            {
                string officialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
                GameFolderSetting[] folders =
                [
                    new(localGameDirectory, LocalizationManager.Get("folder.current"), "folder.current"),
                    new(officialDirectory, LocalizationManager.Get("folder.official"), "folder.official")
                ];
                Settings = Settings with { GameFolders = folders, SelectedGameFolderPath = localGameDirectory };
                _settingsStore.Save(Settings);
            }
            if (!File.Exists(settingsPath))
            {
                _settingsStore.Save(Settings);
            }

            Logger.Information("Settings", $"配置已加载, 主题: {Settings.Theme}, 主题色: {Settings.AccentColor}, 不透明度: {Settings.WindowOpacity:P0}, 标题栏: {Settings.TitleBarMode}, 版本列表源: {Settings.VersionListSource}, 最大连接数: {Settings.DownloadMaxConnections}, 限速: {Settings.DownloadSpeedLimitMiBPerSecond} MiB/s.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 保留无法解析的配置文件, 只在本次运行中使用默认值.
            Logger.Error("Settings", "读取配置失败, 本次使用默认设置.", exception);
            Settings = new LauncherSettings();
            LocalizationManager.Apply(Settings.Language);
        }

        LauncherThemeManager.Apply(Settings);
        _ = LauncherProfileCompatibility.InitializeFolderAsync(localGameDirectory);
        _serviceCredentials = ServiceCredentials.Load(Path.Combine(dataDirectory, "services.json"));
        _accountStore = new LauncherAccountStore(Path.Combine(dataDirectory, "accounts.json"));
        _skinService = new LauncherSkinService(Path.Combine(dataDirectory, "cache", "skins"));
        Logger.Information("App", $"启动器启动, 核心版本: {CoreInfo.Version}, 数据目录: {dataDirectory}");
        base.OnStartup(e);
        if (Current.MainWindow is { } mainWindow)
            mainWindow.Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag);
        _ = JavaCatalog.InitializeIfEmptyAsync();
        Skins.RefreshAll();
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
            Logger.Error("Settings", $"保存配置失败, 路径: {_settingsStore!.FilePath}.", exception);
            LauncherDialogs.Error(LocalizationManager.Get("app.saveFailed"), string.Format(LocalizationManager.Get("app.saveFailedDetail"), exception.Message));
            return false;
        }

        bool themeChanged = Settings.Theme != normalized.Theme;
        bool languageChanged = Settings.Language != normalized.Language;
        bool paletteChanged = themeChanged || !string.Equals(Settings.AccentColor, normalized.AccentColor, StringComparison.OrdinalIgnoreCase);
        bool titleChanged = Settings.TitleBarMode != normalized.TitleBarMode || Settings.TitleBarText != normalized.TitleBarText;
        MainWindow? window = Current.MainWindow as MainWindow;
        if (themeChanged)
        {
            window?.CaptureThemeTransition();
        }

        Settings = normalized;
        if (languageChanged)
        {
            LocalizationManager.Apply(Settings.Language);
        }
        if (paletteChanged)
        {
            LauncherThemeManager.Apply(Settings);
        }

        if (window is not null)
        {
            window.ApplyContentOpacity(Settings.WindowOpacity);
            if (titleChanged)
            {
                window.ApplyTitleBar(Settings.TitleBarMode, Settings.TitleBarText ?? string.Empty);
            }

            if (themeChanged)
            {
                window.FadeThemeTransition();
            }
        }

        Logger.Information("Settings", $"设置已更新, 主题: {Settings.Theme}, 主题色: {Settings.AccentColor}, 不透明度: {Settings.WindowOpacity:P0}, 标题栏: {Settings.TitleBarMode}, 版本列表源: {Settings.VersionListSource}, 最大连接数: {Settings.DownloadMaxConnections}, 限速: {Settings.DownloadSpeedLimitMiBPerSecond} MiB/s.");
        return true;
    }

    /// <summary>将当前生效的启动器配置导出到用户选择的 JSON 文件.</summary>
    internal static void ExportSettings(string path)
    {
        new JsonFileStore<LauncherSettings>(path, SettingsJsonOptions).Save(Settings);
        Logger.Information("Settings", $"配置已导出: {path}");
    }

    /// <summary>验证外部 JSON, 规范化后原子写入当前配置文件, 供重启后读取.</summary>
    internal static void ImportSettings(string path)
    {
        string json = File.ReadAllText(path);
        LauncherSettings imported = JsonSerializer.Deserialize<LauncherSettings>(json, SettingsJsonOptions)
            ?? throw new JsonException("配置文件内容不能为 null.");
        LauncherSettings normalized = imported.Normalize();
        _settingsStore!.Save(normalized);
        _ = _settingsStore.Load(new LauncherSettings()).Normalize();
        Logger.Information("Settings", $"配置已导入并验证: {path}");
    }

    /// <summary>先启动新进程, 成功后退出当前进程.</summary>
    internal static void Restart()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定启动器程序路径.");
        using Process process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        }) ?? throw new InvalidOperationException("无法启动新的启动器进程.");
        Current.Shutdown();
    }

    /// <summary>只预览窗口透明度, 不在拖动滑块时反复写入配置.</summary>
    /// <param name="opacity">当前滑块对应的不透明度.</param>
    internal static void PreviewWindowOpacity(double opacity)
    {
        if (Current.MainWindow is MainWindow window)
        {
            window.ApplyContentOpacity(opacity);
        }
    }

    /// <summary>预览自定义标题文字, 输入过程中不写入配置.</summary>
    internal static void PreviewTitleBar(TitleBarMode mode, string text)
    {
        if (Current.MainWindow is MainWindow window)
        {
            window.ApplyTitleBar(mode, text);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            DownloadEngine.DisposeAsync().AsTask().GetAwaiter().GetResult();
            DownloadHttpClient.Dispose();
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
