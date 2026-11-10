using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

using ErhioLab.Craft.Launcher.Tasks;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Navigation;

namespace ErhioLab.Craft.Launcher.Views.Tasks;

/// <summary>展示所有下载和安装任务的统一入口.</summary>
public partial class TaskManagerView
{
    private readonly UIElement _overview;
    private readonly AnimatedPageHost _pageHost;
    private readonly GameLogView _gameLog = new();
    internal bool IsShowingGameLog { get; private set; }
    internal event Action? GameLogClosed;

    public TaskManagerView()
    {
        InitializeComponent();
        _overview = (UIElement)Content;
        Content = null;
        _pageHost = new AnimatedPageHost(_overview);
        Content = _pageHost;
        _gameLog.BackRequested += CloseGameLog;
        TaskList.DataContext = App.Tasks;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) UpdateSummary();
        };
        App.Tasks.Tasks.CollectionChanged += Tasks_CollectionChanged;
        LocalizationManager.Changed += (_, _) =>
        {
            foreach (LauncherTask task in App.Tasks.Tasks) task.RefreshLocalization();
            UpdateSummary();
        };
        foreach (LauncherTask task in App.Tasks.Tasks)
        {
            task.PropertyChanged += Task_PropertyChanged;
        }
        UpdateSummary();
    }

    private void GameLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LauncherTask { CanViewLog: true } task }) return;
        _gameLog.ShowTask(task);
        IsShowingGameLog = true;
        _pageHost.Navigate(_gameLog);
    }

    internal void CloseGameLog()
    {
        if (!IsShowingGameLog) return;
        IsShowingGameLog = false;
        _pageHost.Navigate(_overview, back: true);
        App.Logger.Information("GameLogs", "关闭实例日志并返回任务列表.");
        GameLogClosed?.Invoke();
    }

    private void Tasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (LauncherTask task in e.OldItems) task.PropertyChanged -= Task_PropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (LauncherTask task in e.NewItems) task.PropertyChanged += Task_PropertyChanged;
        }
        UpdateSummary();
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LauncherTask.Progress) or nameof(LauncherTask.SpeedBytesPerSecond)
            or nameof(LauncherTask.RemainingFiles) or nameof(LauncherTask.Status))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        LauncherTask[] operations = App.Tasks.Tasks.Where(task => !task.IsRunningGame).ToArray();
        LauncherTask[] active = operations.Where(task => task.IsActive).ToArray();
        int runningGames = App.Tasks.Tasks.Count(task => task.IsRunningGame && task.IsActive);
        bool gamesOnly = operations.Length == 0 && runningGames > 0;
        double progress = active.Length > 0 ? active.Average(task => task.Progress)
            : operations.Length > 0 ? operations.Average(task => task.Progress) : 0;
        OverallProgressBar.Value = progress;
        OverallProgressBar.Visibility = gamesOnly ? Visibility.Collapsed : Visibility.Visible;
        OverallProgressLabel.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty,
            gamesOnly ? "I18n.gameTask.runningSummary" : "I18n.taskManager.overallProgress");
        OverallProgressText.Text = gamesOnly ? runningGames.ToString() : $"{progress:0.0}%";
        long speed = active.Sum(task => task.SpeedBytesPerSecond);
        SpeedText.Text = DataSizeFormatter.Format(speed, App.Settings.MemoryUnit, "0.##", minimumPower: 1) + "/s";
        RemainingFilesText.Text = active.Sum(task => task.RemainingFiles).ToString();
        EmptyText.Visibility = App.Tasks.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void TaskAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherTask task })
        {
            await App.Tasks.CancelOrRemoveAsync(task);
        }
    }
}
