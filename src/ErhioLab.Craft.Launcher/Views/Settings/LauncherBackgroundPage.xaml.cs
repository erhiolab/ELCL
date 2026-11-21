using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Controls;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher.Views.Settings;

public partial class LauncherBackgroundPage
{
    private const int GalleryBatchSize = 24;
    private static readonly int[] IntervalPresets = [10, 15, 30, 60, 120, 300, 600, 1800, 3600];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private MainWindow? _window;
    private bool _binding = true;
    private bool _refreshingWallpaper;
    private bool _galleryReady;
    private int _galleryVersion;
    private int _openVersion;
    private bool ShowWallpaper => WallpaperSource?.IsChecked == true;
    private LauncherBackgroundTile[] _sourceTiles = [];
    private LauncherBackgroundTile[] _tiles = [];
    private CancellationTokenSource? _thumbnailCancellation;
    private Task _thumbnailLoading = Task.CompletedTask;
    internal event Action? BackRequested;

    public LauncherBackgroundPage()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => SaveOptions();
        _searchTimer.Tick += (_, _) => ApplyGalleryFilters(animate: false);
    }

    internal void ReloadOptions()
    {
        RefreshOptions();
        UpdateFileState();
    }

    private void RefreshOptions()
    {
        _saveTimer.Stop();
        _binding = true;
        var options = App.Settings.BackgroundOptions;
        EnabledBox.IsChecked = options.Enabled;
        PlayInBackgroundBox.IsChecked = options.PlayInBackground;
        FitBox.SelectedIndex = (int)options.Fit;
        ModeBox.SelectedIndex = (int)options.PlaybackMode;
        IntervalBox.ItemsSource = IntervalPresets.Append(options.SlideIntervalSeconds).Distinct().Order()
            .Select(seconds => new KeyValuePair<int, string>(seconds, seconds % 60 == 0
                ? string.Format(L("minutes"), seconds / 60)
                : string.Format(L("seconds"), seconds))).ToArray();
        IntervalBox.SelectedValue = options.SlideIntervalSeconds;
        IntervalReveal.IsExpanded = options.PlaybackMode == BackgroundPlaybackMode.Slideshow;
        BackgroundOpacitySlider.Value = options.Opacity;
        CardOpacitySlider.Value = options.CardOpacity;
        BlurSlider.Value = options.BlurRadius;
        VolumeSlider.Value = options.Volume;
        _binding = false;
    }

    private void Card_Loaded(object sender, RoutedEventArgs args)
    {
        _window = Window.GetWindow(this) as MainWindow;
        if (_window is not null)
        {
            _window.BackgroundFilesChanged += LocalFilesChanged;
            _window.WallpaperBackgroundFilesChanged += WallpaperFilesChanged;
            _window.BackgroundDisplayChanged += UpdateFileState;
        }

        LocalizationManager.Changed += Language_Changed;
        RefreshOptions();
        if (App.Settings.BackgroundOptions.SelectedWallpaperId is not null) WallpaperSource.IsChecked = true;
        AgeFilterReveal.IsExpanded = ShowWallpaper;
        UpdateFileState();
    }

    internal void QueueGalleryLoad()
    {
        int version = _openVersion;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (version != _openVersion || _galleryReady || _window is null) return;
            _galleryReady = true;
            LoadCurrentSource(animate: false);
        }, DispatcherPriority.Background);
    }

    private void Card_Unloaded(object sender, RoutedEventArgs args)
    {
        if (_saveTimer.IsEnabled) SaveOptions();
        if (_window is not null)
        {
            _window.BackgroundFilesChanged -= LocalFilesChanged;
            _window.WallpaperBackgroundFilesChanged -= WallpaperFilesChanged;
            _window.BackgroundDisplayChanged -= UpdateFileState;
        }

        _searchTimer.Stop();
        _openVersion++;
        _galleryVersion++;
        _galleryReady = false;
        StopThumbnails();
        _sourceTiles = [];
        _tiles = [];
        Gallery.ItemsSource = null;
        _window = null;
        LocalizationManager.Changed -= Language_Changed;
    }

    private void Language_Changed(object? sender, EventArgs args)
    {
        if (_saveTimer.IsEnabled) SaveOptions();
        RefreshOptions();
        UpdateFileState();
    }

    private void Back_Click(object sender, RoutedEventArgs args) => BackRequested?.Invoke();
    private void Next_Click(object sender, RoutedEventArgs args) => _window?.AdvanceLauncherBackground();

    private void LocalFilesChanged()
    {
        if (_galleryReady && !ShowWallpaper) RebuildGallery();
        else UpdateFileState();
    }

    private void WallpaperFilesChanged()
    {
        if (_galleryReady && ShowWallpaper) RebuildGallery();
        else UpdateFileState();
    }

    private void Source_Checked(object sender, RoutedEventArgs args)
    {
        if (_window is null) return;
        AgeFilterReveal.IsExpanded = ShowWallpaper;
        if (_galleryReady) LoadCurrentSource(animate: true);
        else UpdateFileState();
    }

    private void LoadCurrentSource(bool animate)
    {
        if (ShowWallpaper && _window is { WallpaperBackgroundLoaded: false }) _ = RefreshWallpaperAsync(notify: false);
        RebuildGallery(animate);
    }

    private void GalleryFilter_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_galleryReady) ApplyGalleryFilters(animate: true);
    }

    private void GallerySearch_Changed(object sender, TextChangedEventArgs args)
    {
        if (!_galleryReady) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void RebuildGallery(bool animate = false)
    {
        _sourceTiles = ((ShowWallpaper ? _window?.WallpaperBackgroundFiles : _window?.BackgroundFiles) ?? [])
            .Select(file => new LauncherBackgroundTile(file)).ToArray();
        ApplyGalleryFilters(animate);
    }

    private void ApplyGalleryFilters(bool animate)
    {
        _searchTimer.Stop();
        StopThumbnails();
        bool? isVideo = MediaFilterBox.SelectedIndex switch { 1 => false, 2 => true, _ => null };
        WallpaperAgeRating? rating = (ShowWallpaper ? AgeFilterBox.SelectedIndex : 0) switch
        {
            1 => WallpaperAgeRating.Everyone,
            2 => WallpaperAgeRating.ParentalGuidance,
            3 => WallpaperAgeRating.AdultsOnly,
            4 => WallpaperAgeRating.Unrated,
            _ => null
        };
        var search = GallerySearchBox.Text.Trim();
        _tiles = _sourceTiles.Where(tile => (isVideo is null || tile.File.IsVideo == isVideo)
            && (rating is null || tile.File.AgeRating == rating)
            && (search.Length == 0 || tile.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || tile.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        int version = ++_galleryVersion;
        if (_tiles.Length <= GalleryBatchSize) Gallery.ItemsSource = _tiles;
        else _ = PresentGalleryAsync(_tiles, version);
        UpdateFileState();
        if (animate)
        {
            Gallery.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }

        if (_tiles.All(tile => tile.ThumbnailResolved)) return;
        _thumbnailCancellation = new CancellationTokenSource();
        _thumbnailLoading = LoadThumbnailsAsync(_thumbnailLoading, _tiles, _thumbnailCancellation.Token);
    }

    private async Task PresentGalleryAsync(LauncherBackgroundTile[] tiles, int version)
    {
        // WrapPanel 会立即创建卡片, 分批加入并给布局和输入留出调度机会.
        var visible = new ObservableCollection<LauncherBackgroundTile>(tiles.Take(GalleryBatchSize));
        Gallery.ItemsSource = visible;
        for (int i = GalleryBatchSize; i < tiles.Length;)
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (version != _galleryVersion) return;
            int end = Math.Min(i + GalleryBatchSize, tiles.Length);
            for (; i < end; i++) visible.Add(tiles[i]);
        }
    }

    private void StopThumbnails()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
    }

    private static async Task LoadThumbnailsAsync(Task previous, LauncherBackgroundTile[] tiles, CancellationToken token)
    {
        await previous;
        foreach (var tile in tiles.OrderBy(tile => tile.File.IsVideo))
        {
            if (token.IsCancellationRequested) return;
            if (tile.ThumbnailResolved) continue;
            try
            {
                var thumbnail = tile.File.PreviewPath is { } preview
                    ? await Task.Run(() => LauncherBackgroundPresenter.ReadImage(preview, 192), token)
                    : tile.File.IsVideo
                        ? await Task.Run(() => VideoThumbnailReader.Read(tile.File.Path, 192, token), token)
                        : await Task.Run(() => LauncherBackgroundPresenter.ReadImage(tile.File.Path, 192), token);
                if (!token.IsCancellationRequested) tile.SetThumbnail(thumbnail);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or COMException or OperationCanceledException)
            {
                if (token.IsCancellationRequested) return;
                App.Logger.Error("Background", $"无法生成背景缩略图: {tile.File.Path}.", error);
                tile.MarkThumbnailFailed();
            }
        }
    }

    private void UpdateFileState()
    {
        var local = _window?.BackgroundFiles ?? [];
        var wallpapers = _window?.WallpaperBackgroundFiles ?? [];
        var count = _tiles.Length;
        GalleryCard.Header = count == 0 ? L("gallery") : string.Format(L("galleryCount"), count);
        var options = App.Settings.BackgroundOptions;
        var current = options.Enabled ? _window?.DisplayedBackground : null;
        var wallpaperId = options.PlaybackMode == BackgroundPlaybackMode.Fixed ? options.SelectedWallpaperId : null;
        OptionsReveal.IsExpanded = local.Length + wallpapers.Length > 0;
        OpenFolderButton.Visibility = ShowWallpaper ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = !ShowWallpaper && local.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = L(_sourceTiles.Length != 0 ? "noFilterMatches"
            : !ShowWallpaper ? "emptyLibrary" : !_galleryReady || _refreshingWallpaper ? "wallpaperLoading" : "wallpaperEmpty");
        NextButton.Visibility = options.PlaybackMode == BackgroundPlaybackMode.Fixed ? Visibility.Collapsed : Visibility.Visible;
        NextButton.IsEnabled = options.Enabled && local.Length > 1;
        CurrentBackgroundDetail.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
        if (current is not null)
        {
            CurrentBackgroundLabel.Text = string.Format(L("nowShowing"), current.Title ?? Path.GetFileName(current.RelativePath));
            CurrentBackgroundLabel.ToolTip = current.Title ?? current.RelativePath;
        }

        string? missing = null;
        if (options.Enabled && current is null && options.PlaybackMode == BackgroundPlaybackMode.Fixed)
        {
            if (wallpaperId is not null && _window is { WallpaperBackgroundLoaded: true } && !wallpapers.Any(file => Same(file.WallpaperId, wallpaperId)))
                missing = L("wallpaperMissing");
            else if (wallpaperId is null && options.SelectedRelativePath is { } selected && !local.Any(file => Same(file.RelativePath, selected)))
                missing = string.Format(L("fixedMissing"), selected);
        }

        if (missing is not null) MissingBackgroundLabel.Text = missing;
        MissingBackgroundReveal.IsExpanded = missing is not null;
        foreach (var tile in _tiles)
        {
            var isCurrent = current is not null && Same(tile.File.Path, current.Path) && Same(tile.File.WallpaperId, current.WallpaperId);
            var isFixed = options.PlaybackMode == BackgroundPlaybackMode.Fixed && (tile.File.WallpaperId is { } id
                ? Same(id, wallpaperId) : wallpaperId is null && Same(tile.RelativePath, options.SelectedRelativePath));
            tile.Refresh(isCurrent, isFixed);
        }
    }

    private LauncherBackgroundSettings ReadOptions() => App.Settings.BackgroundOptions with
    {
        Enabled = EnabledBox.IsChecked == true,
        PlayInBackground = PlayInBackgroundBox.IsChecked == true,
        Fit = (BackgroundFit)Math.Max(FitBox.SelectedIndex, 0),
        Opacity = (int)BackgroundOpacitySlider.Value,
        CardOpacity = (int)CardOpacitySlider.Value,
        BlurRadius = (int)BlurSlider.Value,
        Volume = (int)VolumeSlider.Value,
        PlaybackMode = (BackgroundPlaybackMode)Math.Max(ModeBox.SelectedIndex, 0),
        SlideIntervalSeconds = IntervalBox.SelectedValue is int seconds ? seconds : App.Settings.BackgroundOptions.SlideIntervalSeconds
    };

    private void PlaybackOption_Changed(object sender, RoutedEventArgs args) => SaveOptions();
    private void Fit_Changed(object sender, SelectionChangedEventArgs args) => SaveOptions();
    private void Interval_Changed(object sender, SelectionChangedEventArgs args) => SaveOptions();

    private void Mode_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_binding || _window is not { } window) return;
        var options = ReadOptions() with { SelectedWallpaperId = null };
        if (options.PlaybackMode == BackgroundPlaybackMode.Fixed && !window.BackgroundFiles.Any(file => Same(file.RelativePath, options.SelectedRelativePath)))
        {
            var chosen = window.DisplayedBackground is { WallpaperId: null } current ? current : window.BackgroundFiles.FirstOrDefault();
            options = options with { SelectedRelativePath = chosen?.RelativePath };
        }

        CommitOptions(options);
    }

    private void SelectTile_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: LauncherBackgroundTile tile }) return;
        CommitOptions(ReadOptions() with
        {
            PlaybackMode = BackgroundPlaybackMode.Fixed,
            SelectedRelativePath = tile.File.WallpaperId is null ? tile.RelativePath : App.Settings.BackgroundOptions.SelectedRelativePath,
            SelectedWallpaperId = tile.File.WallpaperId
        });
    }

    private void Options_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (_binding || _window is null) return;
        _window.ApplyLauncherBackground(ReadOptions());
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveOptions()
    {
        if (_binding || _window is null) return;
        CommitOptions(ReadOptions());
    }

    private void CommitOptions(LauncherBackgroundSettings options)
    {
        if (_window is not { } window) return;
        _saveTimer.Stop();
        var previous = App.Settings.BackgroundOptions;
        if (options != previous && !App.UpdateSettings(App.Settings with { Background = options }))
        {
            window.ApplyLauncherBackground(previous);
        }
        else
        {
            if (options.PlaybackMode != previous.PlaybackMode || options.SelectedRelativePath != previous.SelectedRelativePath
                || options.SelectedWallpaperId != previous.SelectedWallpaperId)
            {
                App.Logger.Information("Background", $"背景模式已设置: {options.PlaybackMode}, 固定素材: {options.SelectedWallpaperId ?? options.SelectedRelativePath}.");
            }

            if (options.PlayInBackground != previous.PlayInBackground)
                App.Logger.Information("Background", $"后台播放已设置: {options.PlayInBackground}.");
        }

        RefreshOptions();
        UpdateFileState();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs args)
    {
        if (_window is null) return;
        try
        {
            var directory = LauncherBackgroundFiles.EnsureDirectory(LauncherBackgroundFiles.DirectoryPath);
            using var process = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            App.Logger.Information("Background", $"已打开背景目录: {directory}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("Background", "打开背景目录失败.", error);
            LauncherToasts.Error(L("folderFailed"));
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs args)
    {
        if (ShowWallpaper)
        {
            await RefreshWallpaperAsync(notify: true);
            return;
        }
        if (_window is not { } window) return;
        Actions.IsEnabled = false;
        try
        {
            await window.RefreshLauncherBackgroundAsync();
            var count = window.BackgroundFiles.Length;
            LauncherToasts.Information(count == 0 ? L("noFiles") : string.Format(L("refreshed"), count));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Background", "刷新背景目录失败.", error);
            LauncherToasts.Error(L("refreshFailed"));
        }
        finally
        {
            Actions.IsEnabled = true;
        }
    }

    private async Task RefreshWallpaperAsync(bool notify)
    {
        if (_refreshingWallpaper || _window is not { } window) return;
        _refreshingWallpaper = true;
        UpdateFileState();
        try
        {
            await window.RefreshWallpaperBackgroundAsync();
            var count = window.WallpaperBackgroundFiles.Length;
            if (notify) LauncherToasts.Information(count == 0 ? L("wallpaperEmpty") : string.Format(L("refreshed"), count));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            App.Logger.Error("Background", "刷新 Wallpaper Engine 素材失败.", error);
            if (notify) LauncherToasts.Error(L("wallpaperRefreshFailed"));
        }
        finally
        {
            _refreshingWallpaper = false;
            if (_window is not null) UpdateFileState();
        }
    }

    private async void Clear_Click(object sender, RoutedEventArgs args)
    {
        if (ShowWallpaper || _window is not { } window || window.BackgroundFiles.Length == 0) return;
        var files = window.BackgroundFiles;
        Actions.IsEnabled = false;
        try
        {
            if (!await LauncherDialogs.ConfirmAsync(L("clear"), string.Format(L("clearPrompt"), files.Length),
                    confirmText: L("clear"))) return;

            App.Logger.Information("Background", $"开始清空 {files.Length} 个背景文件.");
            StopThumbnails();
            await _thumbnailLoading;
            window.ReleaseLauncherBackground();
            var result = await Task.Run(() => DeleteFiles(files));
            await window.RefreshLauncherBackgroundAsync();
            App.Logger.Information("Background", $"背景清空完成, 删除 {result.Deleted} 个, 失败 {result.Failed} 个.");
            if (result.Failed == 0) LauncherToasts.Success(string.Format(L("cleared"), result.Deleted));
            else LauncherToasts.Error(string.Format(L("partiallyCleared"), result.Deleted, result.Failed));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Background", "清空背景后刷新失败.", error);
            LauncherToasts.Error(L("clearFailed"));
        }
        finally
        {
            Actions.IsEnabled = true;
        }
    }

    private static (int Deleted, int Failed) DeleteFiles(IEnumerable<LauncherBackgroundFile> files)
    {
        var deleted = 0;
        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                if (LauncherBackgroundFiles.TryDelete(LauncherBackgroundFiles.DirectoryPath, file)) deleted++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                App.Logger.Error("Background", $"无法删除背景: {file.Path}.", error);
                failed++;
            }
        }

        return (deleted, failed);
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string L(string key) => LocalizationManager.Get("background." + key);
}
