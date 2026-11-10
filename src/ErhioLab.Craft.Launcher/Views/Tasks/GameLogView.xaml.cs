using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Launching;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Tasks;

namespace ErhioLab.Craft.Launcher.Views.Tasks;

public partial class GameLogView
{
    public static readonly DependencyProperty WrapLinesProperty = DependencyProperty.Register(nameof(WrapLines), typeof(bool), typeof(GameLogView), new PropertyMetadata(true, LayoutChanged));
    public static readonly DependencyProperty ColorOutputProperty = DependencyProperty.Register(nameof(ColorOutput), typeof(bool), typeof(GameLogView), new PropertyMetadata(true));
    public static readonly DependencyProperty RawOutputProperty = DependencyProperty.Register(nameof(RawOutput), typeof(bool), typeof(GameLogView), new PropertyMetadata(false, LayoutChanged));
    internal const int MaxDisplayedLines = 10000;
    private readonly ObservableCollection<GameLogLine> _lines = [];
    private readonly ICollectionView _filteredLines;
    private bool _readyForFiltering;
    private GameLogParser _parser = new();
    private int _retainedCharacters;
    private int _errors;
    private int _warnings;
    private bool _scrollPending;
    private bool _programmaticScroll;
    private bool _layoutPending;
    private ScrollViewer? _scroller;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
    private LauncherTask? _task;
    private string? _filePath;
    private string _fileTitle = "";
    private bool _snapshot;
    private GameLogReader? _reader;
    private CancellationTokenSource? _readCancellation;
    private bool _reading;
    private bool _failed;
    private bool _trimmed;
    private int _revision;
    internal event Action? BackRequested;

    public bool WrapLines
    {
        get => (bool)GetValue(WrapLinesProperty);
        set => SetValue(WrapLinesProperty, value);
    }

    public bool ColorOutput
    {
        get => (bool)GetValue(ColorOutputProperty);
        set => SetValue(ColorOutputProperty, value);
    }

    public bool RawOutput
    {
        get => (bool)GetValue(RawOutputProperty);
        set => SetValue(RawOutputProperty, value);
    }

    private static void LayoutChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var view = (GameLogView)target;
        if (!view.IsLoaded || view._layoutPending) return;
        view._layoutPending = true;
        view.Dispatcher.BeginInvoke(() =>
        {
            view._layoutPending = false;
            bool following = view.AutoScrollBox.IsChecked == true;
            double offset = view._scroller?.VerticalOffset ?? 0;
            var selected = view.LinesList.SelectedItems.Cast<GameLogLine>().ToArray();
            view._programmaticScroll = true;
            // 使虚拟面板放弃未换行时缓存的行宽, 同时保留选中项和阅读位置.
            view._filteredLines.Refresh();
            view.LinesList.UpdateLayout();
            foreach (var item in selected)
                if (view.LinesList.Items.Contains(item))
                    view.LinesList.SelectedItems.Add(item);
            if (view.WrapLines) view._scroller?.ScrollToHorizontalOffset(0);
            if (!following) view._scroller?.ScrollToVerticalOffset(offset);
            view.LinesList.UpdateLayout();
            view._programmaticScroll = false;
            if (following) view.ScrollToTail();
        }, DispatcherPriority.Loaded);
    }

    public GameLogView()
    {
        InitializeComponent();
        _filteredLines = CollectionViewSource.GetDefaultView(_lines);
        _filteredLines.Filter = item => item is GameLogLine line && (SeverityBox.SelectedIndex switch
        {
            1 => line.Level is "Warning" or "Error", 2 => line.Level == "Error", _ => true
        });
        LinesList.ItemsSource = _filteredLines;
        _readyForFiltering = true;
        LinesList.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(ScrollBar_Scroll), handledEventsToo: true);
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += (_, _) => StartReading();
        Unloaded += (_, _) => StopReading();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && IsLoaded) StartReading();
            else if (!IsVisible) StopReading();
        };
    }

    internal void ShowTask(LauncherTask task)
    {
        if (!task.CanViewLog) throw new ArgumentException("运行任务没有对应的游戏日志.", nameof(task));
        StopReading();
        _task = task;
        _snapshot = false;
        ResetDocument(task.GameLogPath!);
        AutoUpdateBox.Visibility = Visibility.Visible;
        BackButton.SetResourceReference(ToolTipProperty, "I18n.gameLogs.back");
        App.Logger.Information("GameLogs", $"打开实例日志: task={task.Id}, PID={task.ProcessId}, 文件={task.GameLogPath}.");
        if (IsLoaded && IsVisible) StartReading();
    }

    internal void ShowFile(string path, string title, bool snapshot)
    {
        StopReading();
        _task = null;
        _fileTitle = title;
        _snapshot = snapshot;
        ResetDocument(path);
        AutoUpdateBox.Visibility = snapshot ? Visibility.Collapsed : Visibility.Visible;
        BackButton.SetResourceReference(ToolTipProperty, "I18n.versionLogs.back");
        if (IsLoaded && IsVisible) StartReading();
    }

    internal void CloseFile()
    {
        StopReading();
        _reader = null;
        _filePath = null;
        _task = null;
        _lines.Clear();
    }

    private void ResetDocument(string path)
    {
        _filePath = path;
        _reader = new GameLogReader(path);
        _parser = new GameLogParser();
        _lines.Clear();
        _retainedCharacters = _errors = _warnings = 0;
        _trimmed = false;
        _failed = false;
        SearchBox.Clear();
        AutoUpdateBox.IsChecked = AutoScrollBox.IsChecked = true;
        UpdateHeader();
    }

    private void StartReading()
    {
        if (_reader is null || _readCancellation is not null) return;
        _readCancellation = new CancellationTokenSource();
        _timer.Start();
        _ = RefreshAsync();
    }

    private void StopReading()
    {
        _timer.Stop();
        _revision++;
        _readCancellation?.Cancel();
        _readCancellation?.Dispose();
        _readCancellation = null;
    }

    private async Task RefreshAsync()
    {
        UpdateHeader();
        if (_reading || _failed || AutoUpdateBox.IsChecked != true || _reader is null || _readCancellation is null) return;
        _reading = true;
        int revision = _revision;
        var reader = _reader;
        var token = _readCancellation.Token;
        try
        {
            reader.ReadFinalLine = _task?.IsActive != true;
            var batch = await reader.ReadAsync(token);
            if (revision != _revision || token.IsCancellationRequested) return;
            _trimmed |= batch.Truncated;
            var entries = _parser.Parse(batch.Lines, batch.Truncated);
            if (batch.Lines.Count == 0 && _task?.IsActive != true)
            {
                entries = entries.Concat(_parser.Flush()).ToArray();
                if (_snapshot) _timer.Stop();
            }

            foreach (var entry in entries)
            {
                _lines.Add(entry);
                _retainedCharacters += entry.RawText.Length + entry.Message.Length;
                if (entry.Level == "Error") _errors++;
                if (entry.Level == "Warning") _warnings++;
                while (_lines.Count > MaxDisplayedLines || _retainedCharacters > 8 * 1024 * 1024)
                {
                    var removed = _lines[0];
                    _retainedCharacters -= removed.RawText.Length + removed.Message.Length;
                    if (removed.Level == "Error") _errors--;
                    if (removed.Level == "Warning") _warnings--;
                    _lines.RemoveAt(0);
                    _trimmed = true;
                }
            }

            if (batch.Lines.Count > 0 && AutoScrollBox.IsChecked == true) ScrollToTail();
            UpdateHeader();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (revision != _revision) return;
            _failed = true;
            EmptyText.Text = L("gameLogs.readFailed");
            App.Logger.Error("GameLogs", $"读取游戏日志失败: {_filePath}.", error);
            LauncherToasts.Error(L("gameLogs.readFailed"));
        }
        finally
        {
            _reading = false;
        }
    }

    private void UpdateHeader()
    {
        if (_filePath is null) return;
        TitleText.Text = string.Format(L("gameLogs.title"), _task?.Title ?? _fileTitle);
        TitleText.ToolTip = _task?.Title ?? _fileTitle;
        StateText.Text = (_task is not null
                             ? string.Format(L("gameLogs.state"), _task.ProcessId, _task.StatusText, _lines.Count)
                             : string.Format(L("versionLogs.fileState"), _lines.Count))
                         + (_trimmed ? "  " + L("gameLogs.limited") : "");
        StateText.ToolTip = _filePath;
        LevelStatsText.Text = string.Format(L("gameLogs.levelStats"), _warnings, _errors);
        EmptyText.Visibility = LinesList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_failed) EmptyText.SetResourceReference(TextBlock.TextProperty, _lines.Count == 0 ? "I18n.gameLogs.empty" : "I18n.gameLogs.filteredEmpty");
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void ScrollToTail()
    {
        if (_scrollPending) return;
        _scrollPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _scrollPending = false;
            if (AutoScrollBox.IsChecked != true || LinesList.Items.Count == 0 || !IsVisible) return;
            _programmaticScroll = true;
            LinesList.ScrollIntoView(LinesList.Items[^1]);
            LinesList.UpdateLayout();
            _scroller?.ScrollToEnd();
            LinesList.UpdateLayout();
            _programmaticScroll = false;
        }, DispatcherPriority.Loaded);
    }

    private void AutoScroll_Checked(object sender, RoutedEventArgs e)
    {
        if (LinesList is not null && IsLoaded) ScrollToTail();
    }

    private void Tail_Click(object sender, RoutedEventArgs e)
    {
        AutoScrollBox.IsChecked = true;
        ScrollToTail();
    }

    private void Lines_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0 && _scroller?.ScrollableHeight > 0) AutoScrollBox.IsChecked = false;
    }

    private void Lines_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer viewer) return;
        _scroller = viewer;
        if (_programmaticScroll || e.VerticalChange == 0) return;
        bool atBottom = viewer.ScrollableHeight - viewer.VerticalOffset <= 1;
        if (e.VerticalChange > 0 && atBottom) AutoScrollBox.IsChecked = true;
    }

    private void ScrollBar_Scroll(object sender, ScrollEventArgs e)
    {
        if (e.OriginalSource is not ScrollBar { Orientation: Orientation.Vertical }) return;
        AutoScrollBox.IsChecked = false;
        Dispatcher.BeginInvoke(() =>
        {
            if (_scroller is not null) AutoScrollBox.IsChecked = _scroller.ScrollableHeight - _scroller.VerticalOffset <= 1;
        }, DispatcherPriority.Loaded);
    }

    private void Lines_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Copy_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.PageUp or Key.Home) AutoScrollBox.IsChecked = false;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var selected = LinesList.SelectedItems.Cast<GameLogLine>().ToHashSet();
        var text = _filteredLines.Cast<GameLogLine>().Where(line => selected.Count == 0 || selected.Contains(line))
            .Select(line => RawOutput ? line.RawText : line.Text).ToArray();
        if (text.Length == 0) return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, text));
            App.Logger.Information("GameLogs", $"已复制实例日志: task={_task?.Id}, 行数={text.Length}.");
            LauncherToasts.Success(L("gameLogs.copied"));
        }
        catch (ExternalException error)
        {
            App.Logger.Error("GameLogs", "无法复制实例日志.", error);
            LauncherToasts.Error(L("gameLogs.copyFailed"));
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        GameLogReader? reader;
        try
        {
            reader = _filePath is { } path ? new GameLogReader(path, startAtEnd: true) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("GameLogs", "无法清空实例日志显示.", error);
            LauncherToasts.Error(L("gameLogs.readFailed"));
            return;
        }

        _revision++;
        _lines.Clear();
        _parser = new GameLogParser();
        _retainedCharacters = _errors = _warnings = 0;
        _trimmed = false;
        _failed = false;
        _reader = reader;
        UpdateHeader();
        App.Logger.Information("GameLogs", $"已清空游戏日志显示, 保留磁盘文件: {_filePath}.");
        LauncherToasts.Success(L("gameLogs.cleared"));
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath is not { } path) return;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException(null, path);
            App.Logger.Information("GameLogs", $"打开实例日志文件: {path}.");
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false });
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            App.Logger.Error("GameLogs", "无法打开实例日志文件.", error);
            LauncherToasts.Error(L("gameLogs.openFailed"));
        }
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Find_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        var visible = _filteredLines.Cast<GameLogLine>().ToArray();
        if (string.IsNullOrEmpty(SearchBox.Text) || visible.Length == 0) return;
        int start = Math.Max(0, LinesList.SelectedIndex + 1);
        for (int offset = 0; offset < visible.Length; offset++)
        {
            int index = (start + offset) % visible.Length;
            string text = RawOutput ? visible[index].RawText : visible[index].Text;
            if (!text.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)) continue;
            AutoScrollBox.IsChecked = false;
            LinesList.SelectedItem = visible[index];
            LinesList.ScrollIntoView(visible[index]);
            return;
        }

        LauncherToasts.Information(L("gameLogs.notFound"));
    }

    private void Severity_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_readyForFiltering) return;
        _filteredLines.Refresh();
        UpdateHeader();
        if (AutoScrollBox.IsChecked == true) ScrollToTail();
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
