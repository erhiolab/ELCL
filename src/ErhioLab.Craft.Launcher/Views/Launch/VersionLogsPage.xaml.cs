using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ErhioLab.Craft.Launcher.Launching;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Views.Tasks;

namespace ErhioLab.Craft.Launcher.Views.Launch;

public partial class VersionLogsPage
{
    private readonly AnimatedPageHost _host;
    private readonly GameLogView _viewer = new();
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _openCancellation;
    private OpenedVersionLog? _opened;
    private string? _directory;
    private bool _showingLog;

    public VersionLogsPage()
    {
        InitializeComponent();
        Content = null;
        _host = new AnimatedPageHost(Overview);
        Content = _host;
        _viewer.BackRequested += () => CloseLog();
        Loaded += async (_, _) =>
        {
            if (IsVisible) await RefreshAsync();
        };
        Unloaded += (_, _) => Deactivate();
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible) Deactivate();
            else if (IsLoaded) await RefreshAsync();
        };
        if (Application.Current is { } app) app.Exit += (_, _) => Deactivate();
    }

    internal void SetVersionDirectory(string versionDirectory)
    {
        string directory = Path.Combine(versionDirectory, "logs");
        if (string.Equals(directory, _directory, StringComparison.OrdinalIgnoreCase)) return;
        Deactivate();
        _directory = directory;
        DirectoryText.Text = directory;
        DirectoryText.ToolTip = directory;
        FilesList.ItemsSource = null;
        EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.versionLogs.empty");
        EmptyText.Visibility = Visibility.Visible;
        if (IsLoaded && IsVisible) _ = RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(notify: true);

    private async Task RefreshAsync(bool notify = false)
    {
        if (_directory is not { } directory || _scanCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        RefreshButton.IsEnabled = false;
        var selected = (FilesList.SelectedItem as VersionLogListItem)?.File;
        App.Logger.Information("GameLogs", $"开始扫描版本日志: {directory}.");
        try
        {
            var files = await Task.Run(() => VersionLogFiles.Scan(directory, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var items = files.Select(file => new VersionLogListItem(file)).ToArray();
            FilesList.ItemsSource = items;
            FilesList.SelectedItem = items.FirstOrDefault(item => selected is not null && item.File.Path == selected.Path && item.File.EntryIndex == selected.EntryIndex) ?? items.FirstOrDefault();
            EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.versionLogs.empty");
            EmptyText.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            App.Logger.Information("GameLogs", $"版本日志扫描完成: {directory}, 共 {files.Count} 项.");
            if (notify) LauncherToasts.Success(string.Format(L("versionLogs.refreshed"), files.Count));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (cancellation.IsCancellationRequested) return;
            FilesList.ItemsSource = null;
            EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.versionLogs.scanFailed");
            EmptyText.Visibility = Visibility.Visible;
            App.Logger.Error("GameLogs", $"扫描版本日志失败: {directory}.", error);
            LauncherToasts.Error(L("versionLogs.scanFailed"));
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, cancellation))
            {
                _scanCancellation = null;
                RefreshButton.IsEnabled = true;
            }
        }
    }

    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOpenButton();
    private void UpdateOpenButton() => OpenButton.IsEnabled = FilesList.SelectedItem is VersionLogListItem && _openCancellation is null;
    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private async void Files_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(FilesList, source) is ListBoxItem)
        {
            e.Handled = true;
            await OpenSelectedAsync();
        }
    }

    private async void Files_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await OpenSelectedAsync();
    }

    private async Task OpenSelectedAsync()
    {
        if (FilesList.SelectedItem is not VersionLogListItem item || _openCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _openCancellation = cancellation;
        UpdateOpenButton();
        OpenedVersionLog? opened = null;
        App.Logger.Information("GameLogs", $"开始打开版本日志: {item.File.Path}, 成员={item.File.EntryName}.");
        LauncherToasts.Information(L("versionLogs.opening"));
        try
        {
            string cacheRoot = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "game-logs");
            opened = await Task.Run(() => VersionLogFiles.OpenAsync(item.File, cacheRoot, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _viewer.ShowFile(opened.Path, item.Name, opened.IsTemporary);
            _opened = opened;
            opened = null;
            _showingLog = true;
            _host.Navigate(_viewer);
            App.Logger.Information("GameLogs", $"版本日志已打开: {item.Name}, 临时文件={_opened.IsTemporary}.");
            LauncherToasts.Success(L("versionLogs.opened"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (cancellation.IsCancellationRequested) return;
            App.Logger.Error("GameLogs", $"打开版本日志失败: {item.File.Path}, 成员={item.File.EntryName}.", error);
            LauncherToasts.Error(L("versionLogs.openFailed"));
        }
        finally
        {
            Release(opened);
            if (ReferenceEquals(_openCancellation, cancellation))
            {
                _openCancellation = null;
                UpdateOpenButton();
            }
        }
    }

    internal bool CloseLog()
    {
        if (!_showingLog) return false;
        _showingLog = false;
        _viewer.CloseFile();
        Release(_opened);
        _opened = null;
        _host.Navigate(Overview, back: true);
        App.Logger.Information("GameLogs", "关闭版本日志并返回日志列表.");
        return true;
    }

    private void Deactivate()
    {
        _scanCancellation?.Cancel();
        _scanCancellation = null;
        _openCancellation?.Cancel();
        _openCancellation = null;
        CloseLog();
        RefreshButton.IsEnabled = true;
        UpdateOpenButton();
    }

    private static void Release(OpenedVersionLog? opened)
    {
        if (opened is null) return;
        try
        {
            opened.Dispose();
            if (opened.IsTemporary) App.Logger.Information("GameLogs", $"已清理日志解压缓存: {opened.Path}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("GameLogs", $"无法清理日志解压缓存: {opened.Path}.", error);
            LauncherToasts.Error(L("versionLogs.cleanupFailed"));
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_directory is not { } directory) return;
        try
        {
            if (!Directory.Exists(directory))
            {
                LauncherToasts.Information(L("versionLogs.noFolder"));
                return;
            }

            App.Logger.Information("GameLogs", $"打开版本日志目录: {directory}.");
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            App.Logger.Error("GameLogs", $"打开版本日志目录失败: {directory}.", error);
            LauncherToasts.Error(L("versionSelector.openFolderFailed"));
        }
    }

    private static string L(string key) => LocalizationManager.Get(key);
}

internal sealed record VersionLogListItem(VersionLogFile File)
{
    public string Name => File.Name;
    public string Details => $"{File.Modified:g} · {DataSizeFormatter.Format(File.Size, App.Settings.MemoryUnit, "0.##", minimumPower: 0)}";
}
