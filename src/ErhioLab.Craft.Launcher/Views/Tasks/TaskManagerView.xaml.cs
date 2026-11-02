using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Tasks;

namespace ErhioLab.Craft.Launcher.Views.Tasks;

/// <summary>展示所有下载和安装任务的统一入口.</summary>
public partial class TaskManagerView
{
    public TaskManagerView()
    {
        InitializeComponent();
        TaskList.DataContext = App.Tasks;
        App.Tasks.Tasks.CollectionChanged += Tasks_CollectionChanged;
        foreach (LauncherTask task in App.Tasks.Tasks)
        {
            task.PropertyChanged += Task_PropertyChanged;
        }
        UpdateSummary();
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
        LauncherTask[] active = App.Tasks.Tasks.Where(task => task.IsActive).ToArray();
        double progress = active.Length > 0 ? active.Average(task => task.Progress)
            : App.Tasks.Tasks.Count > 0 ? App.Tasks.Tasks.Average(task => task.Progress) : 0;
        OverallProgressBar.Value = progress;
        OverallProgressText.Text = $"{progress:0.0}%";
        long speed = active.Sum(task => task.SpeedBytesPerSecond);
        SpeedText.Text = speed >= 1024 * 1024 ? $"{speed / (1024.0 * 1024):0.##} MiB/s" : $"{speed / 1024.0:0.##} KiB/s";
        RemainingFilesText.Text = active.Sum(task => task.RemainingFiles).ToString();
        EmptyText.Visibility = App.Tasks.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TaskAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherTask task })
        {
            App.Tasks.CancelOrRemove(task);
        }
    }
}
