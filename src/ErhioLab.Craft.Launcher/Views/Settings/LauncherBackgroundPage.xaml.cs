using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private MainWindow? _window;
    private bool _binding = true;
    private bool _refreshingWallpaper;
    private bool _galleryReady;
    private int _galleryVersion;
    private int _openVersion;
    private bool ShowWallpaper => WallpaperSource?.IsChecked == true;
    private bool ShowFavorites => FavoriteSource?.IsChecked == true;
    private bool ShowLocal => !ShowWallpaper && !ShowFavorites;
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
        var options = App.BackgroundSettings;
        EnabledBox.IsChecked = options.Enabled;
        PlayInBackgroundBox.IsChecked = options.PlayInBackground;
        FitBox.SelectedIndex = (int)options.Fit;
        var source = options.ActiveCategory switch
        {
            BackgroundCategory.Favorites => FavoriteSource,
            BackgroundCategory.Wallpaper => WallpaperSource,
            _ => LocalSource
        };
        source.IsChecked = true;
        bool fixedOnly = options.ActiveCategory == BackgroundCategory.Wallpaper;
        RandomModeItem.Visibility = SlideModeItem.Visibility = fixedOnly ? Visibility.Collapsed : Visibility.Visible;
        ModeBox.IsEnabled = !fixedOnly;
        ModeBox.SelectedIndex = (int)options.PlaybackMode;
        AgeFilterReveal.IsExpanded = fixedOnly;
        if (!IntervalBox.IsKeyboardFocused) IntervalBox.Text = FormatInterval(options.SlideIntervalSeconds);
        IntervalReveal.IsExpanded = options.PlaybackMode == BackgroundPlaybackMode.Slideshow;
        BackgroundOpacitySlider.Value = options.Opacity;
        CardOpacitySlider.Value = options.CardOpacity;
        NavigationOpacitySlider.Value = options.NavigationOpacity;
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

    private void Previous_Click(object sender, RoutedEventArgs args) => _window?.PreviousLauncherBackground();

    private void LocalFilesChanged()
    {
        if (_galleryReady && !ShowWallpaper) RebuildGallery();
        else UpdateFileState();
    }

    private void WallpaperFilesChanged()
    {
        if (_galleryReady && !ShowLocal) RebuildGallery();
        else UpdateFileState();
    }

    private void Source_Checked(object sender, RoutedEventArgs args)
    {
        if (_binding || _window is null) return;
        var category = ReferenceEquals(sender, FavoriteSource) ? BackgroundCategory.Favorites
            : ReferenceEquals(sender, WallpaperSource) ? BackgroundCategory.Wallpaper : BackgroundCategory.Local;
        CommitOptions(ReadOptions() with { Category = category });
        if (_galleryReady) LoadCurrentSource(animate: true);
        else UpdateFileState();
    }

    private void LoadCurrentSource(bool animate)
    {
        if (_window is { WallpaperBackgroundLoaded: false } && (ShowWallpaper || ShowFavorites && App.BackgroundSettings.HasWallpaperFavorites))
            _ = RefreshWallpaperAsync(notify: false);
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
        var local = _window?.BackgroundFiles ?? [];
        var wallpaper = _window?.WallpaperBackgroundFiles ?? [];
        var files = App.BackgroundSettings.ResolveCategory(local, wallpaper);
        var existing = new Dictionary<string, LauncherBackgroundTile>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in _sourceTiles) existing.TryAdd(tile.File.FavoriteKey, tile);
        _sourceTiles = files.Select(file => existing.TryGetValue(file.FavoriteKey, out var tile) && tile.File == file
            ? tile : new LauncherBackgroundTile(file)).ToArray();
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
        var options = App.BackgroundSettings;
        var current = options.Enabled ? _window?.DisplayedBackground : null;
        OptionsReveal.IsExpanded = local.Length + wallpapers.Length > 0;
        OpenFolderButton.Visibility = ShowWallpaper ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = ShowLocal && local.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = L(_sourceTiles.Length != 0 ? "noFilterMatches" : ShowLocal ? "emptyLibrary"
            : _refreshingWallpaper || ShowWallpaper && !_galleryReady ? "wallpaperLoading"
            : ShowFavorites ? "emptyFavorites" : "wallpaperEmpty");
        PlaybackNavigation.Visibility = _window?.CanAdvanceLauncherBackground == true ? Visibility.Visible : Visibility.Collapsed;
        CurrentBackgroundDetail.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
        if (current is not null)
        {
            CurrentBackgroundLabel.Text = string.Format(L("nowShowing"), current.Title ?? Path.GetFileName(current.RelativePath));
            CurrentBackgroundLabel.ToolTip = current.Title ?? current.RelativePath;
        }

        string? missing = null;
        if (options.Enabled && current is null && options.PlaybackMode == BackgroundPlaybackMode.Fixed)
        {
            if (ShowWallpaper && _window is { WallpaperBackgroundLoaded: true })
                missing = options.FixedKey is { } key && !wallpapers.Any(file => Same(file.FavoriteKey, key)) ? L("wallpaperMissing")
                    : options.FixedKey is null && wallpapers.Length != 0 ? L("chooseWallpaper") : null;
            else if (ShowLocal && options.SelectedRelativePath is { } selected && !local.Any(file => Same(file.RelativePath, selected)))
                missing = string.Format(L("fixedMissing"), selected);
            else if (ShowFavorites && options.FixedKey is { } favoriteKey && !_refreshingWallpaper
                     && (!options.HasWallpaperFavorites || _window is { WallpaperBackgroundLoaded: true })
                     && !options.ResolveFavorites(local, wallpapers).Any(file => Same(file.FavoriteKey, favoriteKey)))
                missing = L("favoriteFixedMissing");
        }
        else if (options.Enabled && current is null && ShowFavorites && _galleryReady && !_refreshingWallpaper
                 && (!options.HasWallpaperFavorites || _window is { WallpaperBackgroundLoaded: true })
                 && options.ResolveFavorites(local, wallpapers).Length == 0)
            missing = L("favoritePlaybackEmpty");

        if (missing is not null) MissingBackgroundLabel.Text = missing;
        MissingBackgroundReveal.IsExpanded = missing is not null;
        var favorites = options.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in _tiles)
        {
            var isCurrent = current is not null && Same(tile.File.Path, current.Path) && Same(tile.File.WallpaperId, current.WallpaperId);
            var isFixed = options.PlaybackMode == BackgroundPlaybackMode.Fixed && Same(tile.File.FavoriteKey, options.FixedKey);
            tile.Refresh(isCurrent, isFixed, favorites.Contains(tile.File.FavoriteKey));
        }
    }

    private LauncherBackgroundSettings ReadOptions() => App.BackgroundSettings with
    {
        Enabled = EnabledBox.IsChecked == true,
        PlayInBackground = PlayInBackgroundBox.IsChecked == true,
        Fit = (BackgroundFit)Math.Max(FitBox.SelectedIndex, 0),
        Opacity = (int)BackgroundOpacitySlider.Value,
        CardOpacity = (int)CardOpacitySlider.Value,
        NavigationOpacity = (int)NavigationOpacitySlider.Value,
        BlurRadius = (int)BlurSlider.Value,
        Volume = (int)VolumeSlider.Value
    };

    private void PlaybackOption_Changed(object sender, RoutedEventArgs args) => SaveOptions();
    private void Fit_Changed(object sender, SelectionChangedEventArgs args) => SaveOptions();
    private void Interval_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args) => SaveInterval();

    private void Interval_KeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        SaveInterval();
        args.Handled = true;
    }

    private void SaveInterval()
    {
        if (_binding || _window is null) return;
        var previous = App.BackgroundSettings.SlideIntervalSeconds;
        if (!TryParseInterval(IntervalBox.Text, out int seconds))
        {
            IntervalBox.Text = FormatInterval(previous);
            LauncherToasts.Error(L("intervalInvalid"));
            return;
        }
        if (seconds != previous) CommitOptions(ReadOptions() with { SlideIntervalSeconds = seconds });
        IntervalBox.Text = FormatInterval(App.BackgroundSettings.SlideIntervalSeconds);
    }

    private static string FormatInterval(int seconds) => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}");

    private static bool TryParseInterval(string input, out int seconds)
    {
        var text = input.AsSpan().Trim();
        int colon = text.IndexOf(':');
        seconds = 0;
        if (colon < 0)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seconds)) return false;
        }
        else
        {
            if (!int.TryParse(text[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes > 60
                || !int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int remainder) || remainder > 59)
                return false;
            seconds = minutes * 60 + remainder;
        }
        return seconds is >= LauncherBackgroundSettings.MinimumIntervalSeconds and <= LauncherBackgroundSettings.MaximumIntervalSeconds;
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_binding || _window is not { } window || ShowWallpaper || ModeBox.SelectedIndex < 0) return;
        var options = ReadOptions().WithMode((BackgroundPlaybackMode)ModeBox.SelectedIndex);
        if (options.PlaybackMode == BackgroundPlaybackMode.Fixed)
        {
            var files = options.ResolveCategory(window.BackgroundFiles, window.WallpaperBackgroundFiles);
            var currentKey = window.DisplayedBackground?.FavoriteKey;
            var chosen = files.FirstOrDefault(file => Same(file.FavoriteKey, currentKey))
                ?? files.FirstOrDefault(file => Same(file.FavoriteKey, options.FixedKey)) ?? files.FirstOrDefault();
            if (chosen is not null) options = options.WithFixedFile(chosen);
        }

        CommitOptions(options);
    }

    private void SelectTile_Click(object sender, RoutedEventArgs args)
    {
        if (_window is not { } window || sender is not Button { DataContext: LauncherBackgroundTile tile }) return;
        var options = ReadOptions();
        if (options.PlaybackMode == BackgroundPlaybackMode.Fixed) CommitOptions(options.WithFixedFile(tile.File));
        else window.ShowLauncherBackgroundFile(tile.File);
    }

    private void Favorite_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { DataContext: LauncherBackgroundTile tile } box) return;
        var options = ReadOptions();
        var key = tile.File.FavoriteKey;
        bool add = box.IsChecked == true;
        var favorites = add ? options.Favorites.Append(key) : options.Favorites.Where(existing => !Same(existing, key));
        bool saved = CommitOptions(options with { Favorites = favorites.ToArray() });
        if (saved)
        {
            App.Logger.Information("Background", $"{(add ? "已加入" : "已取消")}背景收藏: {key}.");
            if (ShowFavorites) RebuildGallery(animate: true);
        }
        else box.SetCurrentValue(ToggleButton.IsCheckedProperty, tile.IsFavorite);
    }

    private void AppearanceFields_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!args.WidthChanged || sender is not UniformGrid fields) return;
        // 每列保留足够的拖动空间; 窗口变窄时优先减少列数.
        int columns = args.NewSize.Width >= 680 ? 3 : args.NewSize.Width >= 450 ? 2 : 1;
        if (fields.Columns != columns) fields.Columns = columns;
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

    private bool CommitOptions(LauncherBackgroundSettings options)
    {
        if (_window is not { } window) return false;
        _saveTimer.Stop();
        var previous = App.BackgroundSettings;
        bool saved = options == previous || App.UpdateBackgroundSettings(options);
        if (!saved)
        {
            window.ApplyLauncherBackground(previous);
        }
        else
        {
            if (options.ActiveCategory != previous.ActiveCategory || options.PlaybackMode != previous.PlaybackMode || options.FixedKey != previous.FixedKey)
            {
                App.Logger.Information("Background", $"背景播放已设置: 分类={options.ActiveCategory}, 方式={options.PlaybackMode}, 固定素材={options.FixedKey}.");
            }

            if (options.PlayInBackground != previous.PlayInBackground)
                App.Logger.Information("Background", $"后台播放已设置: {options.PlayInBackground}.");
            if (options.SlideIntervalSeconds != previous.SlideIntervalSeconds)
                App.Logger.Information("Background", $"背景切换间隔已设置: {options.SlideIntervalSeconds} 秒.");
        }

        RefreshOptions();
        UpdateFileState();
        return saved;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs args) => MainWindow.OpenLauncherBackgroundFolder();

    private async void Refresh_Click(object sender, RoutedEventArgs args)
    {
        if (ShowWallpaper)
        {
            await RefreshWallpaperAsync(notify: true);
            return;
        }
        if (_window is not { } window) return;
        bool favorites = ShowFavorites;
        Actions.IsEnabled = false;
        try
        {
            if (favorites && App.BackgroundSettings.HasWallpaperFavorites)
                await Task.WhenAll(window.RefreshLauncherBackgroundAsync(), window.RefreshWallpaperBackgroundAsync());
            else await window.RefreshLauncherBackgroundAsync();
            var count = favorites
                ? App.BackgroundSettings.ResolveFavorites(window.BackgroundFiles, window.WallpaperBackgroundFiles).Length
                : window.BackgroundFiles.Length;
            LauncherToasts.Information(count == 0 ? L(favorites ? "emptyFavorites" : "noFiles") : string.Format(L("refreshed"), count));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            App.Logger.Error("Background", favorites ? "刷新背景收藏夹失败." : "刷新背景目录失败.", error);
            LauncherToasts.Error(L(favorites ? "favoriteRefreshFailed" : "refreshFailed"));
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
        if (!ShowLocal || _window is not { } window || window.BackgroundFiles.Length == 0) return;
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
