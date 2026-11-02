using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ErhioLab.Craft.Launcher.Core.Downloads;

namespace ErhioLab.Craft.Launcher.Tasks;

/// <summary>启动器任务与阶段的运行状态.</summary>
internal enum LauncherTaskStatus { Queued, Running, Completed, Failed, Canceled }

/// <summary>任务管理界面显示的一个阶段.</summary>
internal sealed class LauncherTaskStage(string name, Func<LauncherTaskContext, Task> execute) : INotifyPropertyChanged
{
    private LauncherTaskStatus _status = LauncherTaskStatus.Queued;
    private double _progress;

    internal Func<LauncherTaskContext, Task> Execute { get; } = execute;
    public string Name { get; } = name;
    public LauncherTaskStatus Status { get => _status; internal set { _status = value; Changed(); Changed(nameof(StatusText)); } }
    public double Progress { get => _progress; internal set { _progress = value; Changed(); Changed(nameof(ProgressText)); } }
    public string StatusText => Status switch
    {
        LauncherTaskStatus.Completed => "✓",
        LauncherTaskStatus.Failed => "×",
        LauncherTaskStatus.Canceled => "—",
        LauncherTaskStatus.Running => ProgressText,
        _ => "···"
    };
    public string ProgressText => $"{Progress:0}%";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>一个可跨页面观察并取消的启动器操作.</summary>
internal sealed class LauncherTask(string title, IEnumerable<LauncherTaskStage> stages, string? targetPath = null) : INotifyPropertyChanged
{
    private LauncherTaskStatus _status = LauncherTaskStatus.Queued;
    private double _progress;
    private long _speed;
    private int _remainingFiles;
    private string? _error;

    internal CancellationTokenSource Cancellation { get; } = new();
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; } = title;
    public string? TargetPath { get; } = targetPath;
    public ObservableCollection<LauncherTaskStage> Stages { get; } = new(stages);
    public LauncherTaskStatus Status { get => _status; internal set { _status = value; Changed(); Changed(nameof(StatusText)); Changed(nameof(IsActive)); Changed(nameof(ActionText)); } }
    public double Progress { get => _progress; internal set { _progress = value; Changed(); Changed(nameof(ProgressText)); } }
    public long SpeedBytesPerSecond { get => _speed; internal set { _speed = value; Changed(); } }
    public int RemainingFiles { get => _remainingFiles; internal set { _remainingFiles = value; Changed(); } }
    public string? Error { get => _error; internal set { _error = value; Changed(); } }
    public string ProgressText => $"{Progress:0.0}%";
    public bool IsActive => Status is LauncherTaskStatus.Queued or LauncherTaskStatus.Running;
    public string ActionText => IsActive ? "取消" : "移除";
    public string StatusText => Status switch
    {
        LauncherTaskStatus.Queued => "等待中",
        LauncherTaskStatus.Running => "进行中",
        LauncherTaskStatus.Completed => "已完成",
        LauncherTaskStatus.Canceled => "已取消",
        _ => "失败"
    };

    internal void RefreshProgress()
    {
        Progress = Stages.Count == 0 ? 100 : Stages.Average(stage => stage.Progress);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>一个阶段可使用的通用下载操作及进度上报入口.</summary>
internal sealed class LauncherTaskContext(LauncherTask task, LauncherTaskStage stage)
{
    private long _previousBytes;
    private long _previousTicks = Environment.TickCount64;

    internal async Task DownloadAsync(DownloadTaskRequest request, DownloadOptions options)
    {
        Progress<DownloadProgress> progress = new(UpdateDownloadProgress);
        DownloadTaskHandle handle = App.Downloads.Enqueue(request, options, progress, task.Cancellation.Token);
        await handle.Completion;
    }

    private void UpdateDownloadProgress(DownloadProgress progress)
    {
        if (task.Status != LauncherTaskStatus.Running || stage.Status != LauncherTaskStatus.Running)
        {
            return;
        }

        task.RemainingFiles = Math.Max(0, progress.TotalFiles - progress.CompletedFiles);
        if (progress.TotalBytes is > 0 and var total)
        {
            stage.Progress = Math.Clamp(progress.BytesReceived * 100.0 / total, 0, 99.9);
            task.RefreshProgress();
        }

        long now = Environment.TickCount64;
        long elapsed = now - _previousTicks;
        if (elapsed >= 500)
        {
            task.SpeedBytesPerSecond = Math.Max(0, (progress.BytesReceived - _previousBytes) * 1000 / elapsed);
            _previousBytes = progress.BytesReceived;
            _previousTicks = now;
        }
    }
}
