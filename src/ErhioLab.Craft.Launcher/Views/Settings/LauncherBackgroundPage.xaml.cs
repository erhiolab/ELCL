using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private static readonly int[] IntervalPresets = [10, 15, 30, 60, 120, 300, 600, 1800, 3600];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private MainWindow? _window;
    private bool _binding = true;
    private LauncherBackgroundTile[] _tiles = [];
    private CancellationTokenSource? _thumbnailCancellation;
    private Task _thumbnailLoading = Task.CompletedTask;
    internal event Action? BackRequested;

    public LauncherBackgroundPage()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => SaveOptions();
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
            _window.BackgroundFilesChanged += RebuildGallery;
            _window.BackgroundDisplayChanged += UpdateFileState;
        }

        LocalizationManager.Changed += Language_Changed;
        RefreshOptions();
        RebuildGallery();
    }

    private void Card_Unloaded(object sender, RoutedEventArgs args)
    {
        if (_saveTimer.IsEnabled) SaveOptions();
        if (_window is not null)
        {
            _window.BackgroundFilesChanged -= RebuildGallery;
            _window.BackgroundDisplayChanged -= UpdateFileState;
        }

        StopThumbnails();
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
    private void Appearance_Click(object sender, RoutedEventArgs args) => AppearanceCard.BringIntoView();

    private void RebuildGallery()
    {
        StopThumbnails();
        _tiles = (_window?.BackgroundFiles ?? [])
            .Select(file => new LauncherBackgroundTile(file)).ToArray();
        Gallery.ItemsSource = _tiles;
        UpdateFileState();
        _thumbnailCancellation = new CancellationTokenSource();
        _thumbnailLoading = LoadThumbnailsAsync(_tiles, _thumbnailCancellation.Token);
    }

    private void StopThumbnails()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
    }

    private static async Task LoadThumbnailsAsync(LauncherBackgroundTile[] tiles, CancellationToken token)
    {
        foreach (var tile in tiles.OrderBy(tile => tile.File.IsVideo))
        {
            if (token.IsCancellationRequested) return;
            try
            {
                var thumbnail = tile.File.IsVideo
                    ? await Task.Run(() => VideoThumbnailReader.ReadFirstFrameAsync(tile.File.Path, 192, token), token)
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
                // 封面解码失败不代表视频无法作为背景播放.
                if (!tile.File.IsVideo) tile.MarkUnavailable();
            }
        }
    }

    private void UpdateFileState()
    {
        var count = _window?.BackgroundFiles.Length ?? 0;
        BackgroundCard.Header = count == 0 ? L("title") : string.Format(L("titleCount"), count);
        var options = App.Settings.BackgroundOptions;
        var current = _window?.DisplayedBackground;
        AppearanceCard.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AppearanceButton.IsEnabled = count > 0;
        OptionsReveal.IsExpanded = count > 0;
        ClearButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GalleryHint.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Visibility = options.PlaybackMode == BackgroundPlaybackMode.Fixed ? Visibility.Collapsed : Visibility.Visible;
        NextButton.IsEnabled = options.Enabled && count > 1;
        NowShowing.Text = !options.Enabled
            ? L("disabledStatus")
            : current is not null
                ? string.Format(L("nowShowing"), current.RelativePath)
                : options.PlaybackMode == BackgroundPlaybackMode.Fixed && options.SelectedRelativePath is { } selected && !_tiles.Any(tile => string.Equals(tile.RelativePath, selected, StringComparison.OrdinalIgnoreCase))
                    ? string.Format(L("fixedMissing"), selected)
                    : L("nothingShowing");
        foreach (var tile in _tiles)
        {
            var isCurrent = string.Equals(tile.File.Path, current?.Path, StringComparison.OrdinalIgnoreCase);
            var isFixed = options.PlaybackMode == BackgroundPlaybackMode.Fixed && string.Equals(tile.RelativePath, options.SelectedRelativePath, StringComparison.OrdinalIgnoreCase);
            tile.Refresh(isCurrent, isFixed);
        }
    }

    private LauncherBackgroundSettings ReadOptions() => App.Settings.BackgroundOptions with
    {
        Enabled = EnabledBox.IsChecked == true,
        Fit = (BackgroundFit)Math.Max(FitBox.SelectedIndex, 0),
        Opacity = (int)BackgroundOpacitySlider.Value,
        CardOpacity = (int)CardOpacitySlider.Value,
        BlurRadius = (int)BlurSlider.Value,
        Volume = (int)VolumeSlider.Value,
        PlaybackMode = (BackgroundPlaybackMode)Math.Max(ModeBox.SelectedIndex, 0),
        SlideIntervalSeconds = IntervalBox.SelectedValue is int seconds ? seconds : App.Settings.BackgroundOptions.SlideIntervalSeconds
    };

    private void Enabled_Changed(object sender, RoutedEventArgs args) => SaveOptions();
    private void Fit_Changed(object sender, SelectionChangedEventArgs args) => SaveOptions();
    private void Interval_Changed(object sender, SelectionChangedEventArgs args) => SaveOptions();

    private void Mode_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_binding || _window is not { } window) return;
        var options = ReadOptions();
        if (options.PlaybackMode == BackgroundPlaybackMode.Fixed && !window.BackgroundFiles.Any(file =>
                string.Equals(file.RelativePath, options.SelectedRelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            var chosen = window.DisplayedBackground ?? _tiles.FirstOrDefault()?.File;
            options = options with { SelectedRelativePath = chosen?.RelativePath };
        }

        CommitOptions(options);
    }

    private void SelectTile_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: LauncherBackgroundTile tile }) return;
        CommitOptions(ReadOptions() with { PlaybackMode = BackgroundPlaybackMode.Fixed, SelectedRelativePath = tile.RelativePath });
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
        else if (options.PlaybackMode != previous.PlaybackMode || options.SelectedRelativePath != previous.SelectedRelativePath)
        {
            App.Logger.Information("Background", $"背景模式已设置: {options.PlaybackMode}, 固定文件: {options.SelectedRelativePath}.");
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

    private async void Clear_Click(object sender, RoutedEventArgs args)
    {
        if (_window is not { } window || window.BackgroundFiles.Length == 0) return;
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

    private static string L(string key) => LocalizationManager.Get("background." + key);
}
