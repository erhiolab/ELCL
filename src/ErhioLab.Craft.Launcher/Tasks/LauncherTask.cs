using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Tasks;

/// <summary>启动器任务与阶段的运行状态.</summary>
internal enum LauncherTaskStatus { Queued, Running, Completed, Failed, Canceled }

/// <summary>任务管理界面显示的一个阶段.</summary>
internal sealed class LauncherTaskStage(string name, Func<LauncherTaskContext, Task> execute) : INotifyPropertyChanged
{
    private LauncherTaskStatus _status = LauncherTaskStatus.Queued;
    private double _progress;

    internal Func<LauncherTaskContext, Task> Execute { get; } = execute;
    internal Func<string>? LocalizedName { get; init; }
    internal long PlannedBytes { get; set; } = 1;
    public string Name => LocalizedName?.Invoke() ?? name;
    public LauncherTaskStatus Status { get => _status; internal set { _status = value; Changed(); Changed(nameof(StatusText)); } }
    public double Progress { get => _progress; internal set { _progress = value; Changed(); Changed(nameof(ProgressText)); Changed(nameof(StatusText)); } }
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
    internal void RefreshLocalization() => Changed(nameof(Name));
    private void Changed([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>一个可跨页面观察并取消的启动器操作.</summary>
internal sealed class LauncherTask(string title, IEnumerable<LauncherTaskStage> stages, string? targetPath = null) : INotifyPropertyChanged
{
    private LauncherTaskStatus _status = LauncherTaskStatus.Queued;
    private double _progress;
    private long _speed;
    private int _remainingFiles;
    private string? _error;
    private bool _isStopping;

    internal CancellationTokenSource Cancellation { get; } = new();
    internal Task Completion { get; set; } = Task.CompletedTask;
    internal Func<Task>? CleanupOnRemove { get; set; }
    internal Action? OnCompleted { get; init; }
    internal Func<Task>? StopAsync { get; init; }
    internal string? JavaExecutablePath { get; set; }
    /// <summary>游戏启动准备或运行任务, 不独占整个版本目录.</summary>
    internal bool IsGameLaunch { get; init; }
    public bool IsRunningGame { get; init; }
    public int? ProcessId { get; init; }
    public string? GameLogPath { get; init; }
    public bool CanViewLog => IsRunningGame && GameLogPath is not null;
    public bool IsStopping
    {
        get => _isStopping;
        internal set { _isStopping = value; Changed(); Changed(nameof(StatusText)); Changed(nameof(ActionText)); Changed(nameof(CanInvokeAction)); }
    }
    public bool CanInvokeAction => !IsStopping;
    public string? ProcessText => ProcessId is { } id ? string.Format(LocalizationManager.Get("gameTask.processId"), id) : null;
    public Guid Id { get; } = Guid.NewGuid();
    internal Func<string>? LocalizedTitle { get; set; }
    public string Title => LocalizedTitle?.Invoke() ?? title;
    public string? TargetPath { get; } = targetPath;
    public ObservableCollection<LauncherTaskStage> Stages { get; } = new(stages);
    public LauncherTaskStatus Status { get => _status; internal set { _status = value; Changed(); Changed(nameof(StatusCode)); Changed(nameof(StatusText)); Changed(nameof(IsActive)); Changed(nameof(ActionText)); } }
    public double Progress { get => _progress; internal set { _progress = value; Changed(); Changed(nameof(ProgressText)); } }
    public long SpeedBytesPerSecond { get => _speed; internal set { _speed = value; Changed(); } }
    public int RemainingFiles { get => _remainingFiles; internal set { _remainingFiles = value; Changed(); } }
    public string? Error { get => _error; internal set { _error = value; Changed(); } }
    public string ProgressText => IsRunningGame ? string.Empty : $"{Progress:0.0}%";
    public bool IsActive => Status is LauncherTaskStatus.Queued or LauncherTaskStatus.Running;
    public string ActionText => LocalizationManager.Get(IsRunningGame && IsActive
        ? IsStopping ? "gameTask.stopping" : "gameTask.terminate"
        : IsActive ? "task.cancel" : "task.remove");
    public string StatusCode => Status.ToString();
    public string StatusText => Status switch
    {
        LauncherTaskStatus.Queued => LocalizationManager.Get("task.queued"),
        LauncherTaskStatus.Running => LocalizationManager.Get(IsRunningGame ? IsStopping ? "gameTask.stopping" : "gameTask.running" : "task.running"),
        LauncherTaskStatus.Completed => LocalizationManager.Get("task.completed"),
        LauncherTaskStatus.Canceled => LocalizationManager.Get("task.canceled"),
        _ => LocalizationManager.Get("task.failed")
    };

    internal void RefreshLocalization()
    {
        Changed(nameof(Title));
        Changed(nameof(StatusText));
        Changed(nameof(ActionText));
        Changed(nameof(ProcessText));
        foreach (LauncherTaskStage stage in Stages) stage.RefreshLocalization();
    }

    internal void RefreshProgress()
    {
        double totalBytes = Stages.Sum(stage => (double)stage.PlannedBytes);
        Progress = totalBytes <= 0 ? 100 : Stages.Sum(stage => stage.Progress * stage.PlannedBytes) / totalBytes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>一个阶段可使用的通用下载操作及进度上报入口.</summary>
internal sealed class LauncherTaskContext(LauncherTask task, LauncherTaskStage stage)
{
    private long _previousBytes;
    private long _previousTicks = Environment.TickCount64;
    private long _plannedTotalBytes;
    private long _alreadyCompleteBytes;
    internal CancellationToken CancellationToken => task.Cancellation.Token;
    internal void SetJavaExecutablePath(string? path) => task.JavaExecutablePath = path;

    internal void ReportProgress(double value)
    {
        if (task.Status != LauncherTaskStatus.Running || stage.Status != LauncherTaskStatus.Running) return;
        stage.Progress = Math.Clamp(value, 0, 99.9);
        task.RefreshProgress();
    }

    internal void ConfigureStageWeights(IReadOnlyList<long> weights)
    {
        if (weights.Count != task.Stages.Count) throw new ArgumentException("阶段数量不匹配.", nameof(weights));
        for (int index = 0; index < weights.Count; index++)
            task.Stages[index].PlannedBytes = Math.Max(1, weights[index]);
        task.RefreshProgress();
    }

    internal void ConfigureDownloadPlan(IReadOnlyList<DownloadFileRequest> allFiles, IReadOnlyList<DownloadFileRequest> missingFiles,
        bool preserveStageWeight = false)
    {
        _plannedTotalBytes = allFiles.Sum(EstimatedSize);
        long missingBytes = missingFiles.Sum(EstimatedSize);
        _alreadyCompleteBytes = Math.Max(0, _plannedTotalBytes - missingBytes);
        if (!preserveStageWeight) stage.PlannedBytes = Math.Max(1, _plannedTotalBytes);
        task.RemainingFiles = missingFiles.Count;
        stage.Progress = _plannedTotalBytes == 0 ? 0 : Math.Clamp(_alreadyCompleteBytes * 100.0 / _plannedTotalBytes, 0, 99.9);
        task.RefreshProgress();
    }

    internal static long EstimatedSize(DownloadFileRequest file) => Math.Max(1, file.ExpectedSize ?? 1024 * 1024);

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
        if (_plannedTotalBytes > 0)
        {
            stage.Progress = Math.Clamp((_alreadyCompleteBytes + progress.BytesReceived) * 100.0 / _plannedTotalBytes, 0, 99.9);
            task.RefreshProgress();
        }
        else if (progress.TotalBytes is > 0 and var total)
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
