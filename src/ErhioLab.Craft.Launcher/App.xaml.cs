using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Logging;
using ErhioLab.Craft.Launcher.Accounts;
using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Styles;
using ErhioLab.Craft.Launcher.Tasks;
using ErhioLab.Craft.Launcher.Launching;
using ErhioLab.Craft.Launcher.SystemTools;
using ErhioLab.Craft.Launcher.Analytics;

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
            new JsonStringEnumConverter<GameWindowMode>(), new JsonStringEnumConverter<GameGarbageCollector>(),
            new JsonStringEnumConverter<MemoryUnitSystem>()
        }
    };

    private static SessionFileLogger? _logger;
    private static readonly JsonSerializerOptions BackupJsonOptions = LauncherSettingsBackup.CreateOptions(SettingsJsonOptions);
    private static LauncherDataFile<LauncherSettings>? _settingsStore;
    private static bool _settingsWritable = true;
    private static LauncherAccountStore? _accountStore;
    private static LauncherSkinService? _skinService;
    private static AnalyticsEvents? _analytics;
    private readonly CancellationTokenSource _cacheMaintenance = new();
    private static readonly HttpClient DownloadHttpClient = new();
    private static readonly DownloadManager DownloadEngine = new(DownloadHttpClient, diagnostic =>
    {
        string headers = string.Join(", ", diagnostic.ResponseHeaders.Select(header => $"{header.Key}={header.Value}"));
        _logger?.Warning("DownloadHttp", $"{diagnostic.Phase}: {diagnostic.Method} {diagnostic.Origin}, Range={diagnostic.Range ?? "-"}, HTTP={diagnostic.StatusCode?.ToString() ?? "-"}, 响应头=[{headers}], 详情={diagnostic.Detail ?? "-"}.");
    });
    private static readonly LauncherTaskManager TaskEngine = new();
    private static readonly JavaCatalogManager JavaEngine = new();
    internal static GameLaunchService GameLauncher { get; } = new();
    internal static MemoryOptimizationService MemoryOptimizer { get; } = new();
    internal static AnalyticsEvents? Analytics => _analytics;

    /// <summary>当前已加载并应用的设置.</summary>
    internal static LauncherSettings Settings { get; private set; } = new();
    internal static LauncherBackgroundStore BackgroundStore { get; } = new(Path.Combine(AppContext.BaseDirectory, "ELCL", "background-playback.json"));
    internal static LauncherBackgroundSettings BackgroundSettings => BackgroundStore.Options;
    internal static LauncherFeatureVisibility FeatureVisibility { get; } = new();

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

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 客户端决定数据目录, Core 仅接收日志目录并管理其中的文件.
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL");
        string settingsPath = Path.Combine(dataDirectory, "config.json");
        string localGameDirectory = Path.Combine(AppContext.BaseDirectory, ".minecraft");
        try
        {
            WaitForRestartParent(e.Args);
            if (PrepareFileActivation(e.Args)) return;
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(localGameDirectory);
            _logger = new SessionFileLogger(Path.Combine(dataDirectory, "logs"));
            _settingsStore = new LauncherDataFile<LauncherSettings>(settingsPath, LauncherDataKind.Settings, SettingsJsonOptions);
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

        if (!TryLoadStartupSettings(_settingsStore))
        {
            Shutdown(1);
            return;
        }

        LauncherThemeManager.Apply(Settings);
        if (!EnsureStartupConsent(dataDirectory))
        {
            Shutdown();
            return;
        }

        string javaCatalogPath = Path.Combine(dataDirectory, "java.json");
        try
        {
            JavaCatalog.Load(javaCatalogPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Error("Java", $"读取 Java 列表失败: {javaCatalogPath}.", error);
            LauncherThemeManager.Apply(Settings);
            LauncherDialogs.Error(LocalizationManager.Get("app.startFailed"),
                string.Format(LocalizationManager.Get("java.loadFailed"), javaCatalogPath, error.Message));
            Shutdown(1);
            return;
        }

        if (Settings.GameFolders.Count == 0)
        {
            string officialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            GameFolderSetting[] folders =
            [
                new(localGameDirectory, LocalizationManager.Get("folder.current"), "folder.current"),
                new(officialDirectory, LocalizationManager.Get("folder.official"), "folder.official")
            ];
            Settings = Settings with { GameFolders = folders, SelectedGameFolderPath = localGameDirectory };
            try
            {
                _settingsStore.Save(Settings);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Logger.Error("Settings", "保存初始游戏文件夹失败.", error);
            }
        }

        CleanupResult? cleanupResult = null;
        if (e.Args.Contains("--clear-elcl-cache", StringComparer.Ordinal))
        {
            Logger.Information("Cache", "旧进程已退出, 开始清理 ELCL 缓存.");
            try
            {
                var logger = Logger;
                cleanupResult = await Task.Run(() => LauncherCache.Shared.Sweep(clearAll: true, DateTime.UtcNow,
                    (path, error) => logger.Error("Cache", $"无法清理 ELCL 缓存: {path}.", error)));
                LogCleanup(logger, "手动清理完成", cleanupResult);
            }
            catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
            {
                Logger.Error("Cache", "重启后的缓存清理失败.", error);
                cleanupResult = new CleanupResult(0, 0, 0, 1);
            }
        }

        _ = LauncherProfileCompatibility.InitializeFolderAsync(localGameDirectory);
        _analytics = new AnalyticsEvents(
            () => InstallationIdentity.GetOrCreate(Path.Combine(dataDirectory, "installation-id")), Logger);
        _analytics.SetEnabled(AnonymousAnalyticsEnabled);
        _accountStore = new LauncherAccountStore(Path.Combine(dataDirectory, "accounts.json"));
        BackgroundStore.Load();
        _skinService = new LauncherSkinService(LauncherCache.Shared);
        Logger.Information("App", $"启动器启动, 核心版本: {CoreInfo.Version}, 数据目录: {dataDirectory}");
        base.OnStartup(e);
        // 文件转交与数据初始化通过后才创建窗口, 避免提前退出时仍由 StartupUri 构造界面.
        MainWindow mainWindow = new();
        MainWindow = mainWindow;
        mainWindow.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        mainWindow.Show();
        _ = JavaCatalog.InitializeIfEmptyAsync();
        Skins.RefreshAll();
        _ = _analytics.LauncherOpenedAsync();
        var maintenanceToken = _cacheMaintenance.Token;
        var maintenanceLogger = Logger;
        _ = Task.Run(() => RunCacheMaintenanceAsync(maintenanceLogger, maintenanceToken), maintenanceToken);
        if (cleanupResult is { } cleaned)
            _ = Dispatcher.BeginInvoke(() => ShowCleanupResult(cleaned), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static async Task RunCacheMaintenanceAsync(SessionFileLogger logger, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                var result = LauncherCache.Shared.Sweep(clearAll: false, DateTime.UtcNow,
                    (path, error) => logger.Error("Cache", $"无法回收过期缓存: {path}.", error), token);
                if (result.Files > 0 || result.Failed > 0) LogCleanup(logger, "过期缓存清理完成", result);
                await Task.Delay(TimeSpan.FromHours(6), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static void LogCleanup(SessionFileLogger logger, string action, CleanupResult result) =>
        logger.Information("Cache", $"{action}: bytes={result.Bytes}, files={result.Files}, skipped={result.Skipped}, failed={result.Failed}.");

    internal static void ShowCleanupResult(CleanupResult result)
    {
        string message = string.Format(CultureInfo.CurrentCulture, LocalizationManager.Get("toolbox.cleanupCompleted"),
            result.Files, DataSizeFormatter.Format(result.Bytes, Settings.MemoryUnit, "0.00", minimumPower: 2), result.Skipped, result.Failed);
        if (result.Skipped > 0 || result.Failed > 0) Notifications.LauncherToasts.Information(message);
        else Notifications.LauncherToasts.Success(message);
    }

    /// <summary>保存并立即应用新设置.</summary>
    /// <param name="settings">页面提交的新设置.</param>
    /// <returns>保存成功时为 true.</returns>
    internal static bool UpdateSettings(LauncherSettings settings)
    {
        LauncherSettings normalized = settings.Normalize();
        try
        {
            if (!_settingsWritable) throw new IOException(LocalizationManager.Get("settings.localFileUnreadable"));
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
        bool administratorIconChanged = Settings.HideAdministratorModeIcon != normalized.HideAdministratorModeIcon;
        bool hiddenFeaturesChanged = Settings.HiddenFeatures != normalized.HiddenFeatures;
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
            if (paletteChanged) window.ApplyLauncherBackground(BackgroundSettings);
            if (titleChanged)
            {
                window.ApplyTitleBar(Settings.TitleBarMode, Settings.TitleBarText ?? string.Empty);
            }

            if (themeChanged)
            {
                window.FadeThemeTransition();
            }
        }

        if (hiddenFeaturesChanged)
        {
            Logger.Information("Settings", $"已保存功能隐藏选项: {Settings.HiddenFeatures}.");
            FeatureVisibility.NotifyChanged();
        }

        if (administratorIconChanged)
        {
            window?.ApplyAdministratorModeIcon(Settings.HideAdministratorModeIcon);
            Logger.Information("Settings", $"已保存管理员模式图标隐藏偏好: {Settings.HideAdministratorModeIcon}.");
        }

        Logger.Information("Settings", $"设置已更新, 主题: {Settings.Theme}, 主题色: {Settings.AccentColor}, 不透明度: {Settings.WindowOpacity:P0}, 标题栏: {Settings.TitleBarMode}, 内存单位: {Settings.MemoryUnit}, 版本列表源: {Settings.VersionListSource}, 最大连接数: {Settings.DownloadMaxConnections}, 限速: {Settings.DownloadSpeedLimitMiBPerSecond} MiB/s.");
        return true;
    }

    /// <summary>将当前生效的启动器配置导出到用户选择的 JSON 文件.</summary>
    internal static void ExportSettings(string path)
    {
        ValidateBackupDestination(path);
        new LauncherDataFile<LauncherSettingsBackup>(path, LauncherDataKind.SettingsBackup, BackupJsonOptions).Save(new(Settings));
        Logger.Information("Settings", $"配置已导出: {path}");
    }

    internal static LauncherSettingsBackup ReadSettingsBackup(string path)
    {
        var backup = new LauncherDataFile<LauncherSettingsBackup>(path, LauncherDataKind.SettingsBackup, BackupJsonOptions).Read();
        if (backup.Settings is null) throw new JsonException(LocalizationManager.Get("settings.backupInvalidObject"));
        return backup;
    }

    internal static void ImportSettings(LauncherSettingsBackup backup)
    {
        _settingsStore!.Save(backup.ApplyTo(Settings));
        // 重启前不让旧会话的延迟保存覆盖刚导入的配置.
        _settingsWritable = false;
        Logger.Information("Settings", "启动器配置已导入, 等待重启.");
    }

    internal static bool UpdateBackgroundSettings(LauncherBackgroundSettings options)
    {
        try { BackgroundStore.SaveOptions(options); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Logger.Error("Background", "保存背景配置失败.", error);
            Notifications.LauncherToasts.Error(LocalizationManager.Get("settings.backgroundSaveFailed"));
            return false;
        }

        (Current.MainWindow as MainWindow)?.ApplyLauncherBackground(BackgroundSettings);
        return true;
    }

    internal static void ValidateBackupDestination(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL");
        string relative = Path.GetRelativePath(dataDirectory, fullPath);
        if (!Path.IsPathFullyQualified(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException(LocalizationManager.Get("settings.backupInsideData"));
    }

    /// <summary>先启动新进程, 成功后退出当前进程.</summary>
    internal static void Restart(bool clearCache = false)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定启动器程序路径.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(App).Assembly.GetName().Name + ".dll"));
        using var current = Process.GetCurrentProcess();
        start.ArgumentList.Add("--restart-parent");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        if (clearCache) start.ArgumentList.Add("--clear-elcl-cache");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动新的启动器进程.");
        Current.Shutdown();
    }

    private static void WaitForRestartParent(string[] args)
    {
        int index = Array.IndexOf(args, "--restart-parent");
        if (index < 0 || index + 2 >= args.Length || !int.TryParse(args[index + 1], out int pid)
            || !long.TryParse(args[index + 2], out long ticks) || pid == Environment.ProcessId) return;
        try
        {
            using var parent = Process.GetProcessById(pid);
            if (!parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == ticks && !parent.WaitForExit(15000))
                throw new IOException("等待旧启动器退出超时, 已取消本次重启以保护缓存和日志.");
        }
        catch (ArgumentException) { /* 旧进程已经退出.  */ }
        catch (InvalidOperationException) { /* 查询时旧进程恰好退出.  */ }
        catch (System.ComponentModel.Win32Exception error) { throw new IOException("无法等待旧启动器退出.", error); }
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
        _cacheMaintenance.Cancel();
        _cacheMaintenance.Dispose();
        _fileActivation?.Dispose();
        _analytics?.Dispose();
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
