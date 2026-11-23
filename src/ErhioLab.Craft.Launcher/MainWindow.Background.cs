using System.IO;
using System.Windows;

using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Styles;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher;

public partial class MainWindow
{
    private bool _backgroundInitialized;
    private bool _backgroundClosed;
    private bool _windowMoveSizeActive;
    private int _backgroundScanVersion;
    private Task? _wallpaperRefresh;
    private LauncherBackgroundSettings _backgroundPreview = new();
    internal LauncherBackgroundFile[] BackgroundFiles { get; private set; } = [];
    internal LauncherBackgroundFile[] WallpaperBackgroundFiles { get; private set; } = [];
    internal bool WallpaperBackgroundLoaded { get; private set; }
    internal event Action? BackgroundFilesChanged;
    internal event Action? WallpaperBackgroundFilesChanged;
    internal event Action? BackgroundDisplayChanged;
    internal LauncherBackgroundFile? DisplayedBackground => BackgroundPresenter.DisplayedFile;
    internal void AdvanceLauncherBackground() => BackgroundPresenter.Advance();

    private void InitializeLauncherBackground()
    {
        if (_backgroundInitialized) return;
        _backgroundInitialized = true;
        Activated += (_, _) => UpdateBackgroundPlayback();
        Deactivated += (_, _) => UpdateBackgroundPlayback();
        UpdateBackgroundPlayback();
        BackgroundPresenter.BackgroundChanged += () =>
        {
            UpdateBackgroundSurfaces();
            BackgroundDisplayChanged?.Invoke();
        };
        BackgroundPresenter.MediaReady += file => App.Logger.Information("Background", $"背景已显示: {file.Path}.");
        BackgroundPresenter.MediaRejected += (file, error) => App.Logger.Error("Background", $"无法显示背景: {file.Path}.", error);
        Closed += (_, _) =>
        {
            _backgroundClosed = true;
            _backgroundScanVersion++;
            BackgroundPresenter.Stop();
        };
        ApplyLauncherBackground(App.Settings.BackgroundOptions);
        _ = LoadInitialBackgroundAsync();
    }

    internal void ApplyLauncherBackground(LauncherBackgroundSettings options)
    {
        _backgroundPreview = options;
        BackgroundPresenter.Apply(options);
        UpdateBackgroundPlayback();
        UpdateBackgroundSurfaces();
        if (_backgroundInitialized && options.Enabled && options.PlaybackMode == BackgroundPlaybackMode.Fixed
            && options.SelectedWallpaperId is not null && !WallpaperBackgroundLoaded)
            _ = LoadInitialWallpaperAsync();
    }

    private async Task LoadInitialWallpaperAsync()
    {
        try { await RefreshWallpaperBackgroundAsync(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            App.Logger.Error("Background", "无法读取 Wallpaper Engine 素材.", error);
        }
    }

    internal Task RefreshWallpaperBackgroundAsync() => _wallpaperRefresh is { IsCompleted: false } pending
        ? pending : _wallpaperRefresh = LoadWallpaperBackgroundAsync();

    private async Task LoadWallpaperBackgroundAsync()
    {
        App.Logger.Information("Background", "开始读取 Wallpaper Engine 素材.");
        var (files, usedApi) = await Task.Run(WallpaperEngineCatalog.LoadAsync);
        if (_backgroundClosed) return;
        WallpaperBackgroundFiles = files;
        WallpaperBackgroundLoaded = true;
        BackgroundPresenter.SetWallpaperFiles(files);
        WallpaperBackgroundFilesChanged?.Invoke();
        App.Logger.Information("Background", $"Wallpaper Engine 素材读取完成, 来源: {(usedApi ? "本地接口" : "Steam 目录")}, 数量: {files.Length}.");
    }

    private void UpdateBackgroundSurfaces() => LauncherThemeManager.ApplyBackgroundSurfaces(
        BackgroundPresenter.IsDisplayingBackground, _backgroundPreview.CardOpacity / 100.0,
        _backgroundPreview.NavigationOpacity / 100.0);

    private void SetWindowMoveSizeActive(bool active)
    {
        if (_windowMoveSizeActive == active || _backgroundClosed) return;
        _windowMoveSizeActive = active;
        UpdateBackgroundPlayback();
    }

    private void UpdateBackgroundPlayback() => BackgroundPresenter.SetPlaybackActive(
        !_windowMoveSizeActive && (_backgroundPreview.PlayInBackground || (IsActive && WindowState != WindowState.Minimized)));

    private async Task LoadInitialBackgroundAsync()
    {
        try
        {
            await RefreshLauncherBackgroundAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Background", "启动时读取背景目录失败.", error);
        }
    }

    internal async Task RefreshLauncherBackgroundAsync()
    {
        var version = ++_backgroundScanVersion;
        var directory = LauncherBackgroundFiles.DirectoryPath;
        App.Logger.Information("Background", $"开始刷新背景目录: {directory}.");
        var files = await Task.Run(() => LauncherBackgroundFiles.Scan(directory));
        if (version != _backgroundScanVersion) return;
        BackgroundFiles = files;
        BackgroundPresenter.SetFiles(files);
        BackgroundFilesChanged?.Invoke();
        App.Logger.Information("Background", $"背景目录刷新完成, 找到 {files.Length} 个媒体文件.");
    }

    internal void ReleaseLauncherBackground()
    {
        _backgroundScanVersion++;
        BackgroundPresenter.SetFiles([]);
    }

    private void UpdateVisiblePageLayers()
    {
        var tasks = TaskManagerPage.Visibility == Visibility.Visible;
        var accounts = AccountManagementPage.Visibility == Visibility.Visible;
        PrimaryPageLayer.Visibility = tasks || accounts ? Visibility.Hidden : Visibility.Visible;
        AccountManagementLayer.Visibility = tasks ? Visibility.Hidden : Visibility.Visible;
    }
}
