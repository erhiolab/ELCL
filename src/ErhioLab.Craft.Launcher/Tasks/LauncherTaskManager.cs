using System.Collections.ObjectModel;
using System.IO;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Tasks;

/// <summary>统一管理启动器操作的阶段、进度和取消, 下载器由任务阶段调用.</summary>
internal sealed class LauncherTaskManager
{
    public ObservableCollection<LauncherTask> Tasks { get; } = [];

    /// <summary>创建一个可显示在任务管理页的自定义文件下载任务.</summary>
    internal LauncherTask StartCustomDownload(string destination, Uri source, DownloadOptions options)
    {
        destination = Path.GetFullPath(destination);
        if (Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, destination, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("该文件已有正在进行的下载任务.");
        }

        string fileName = Path.GetFileName(destination);
        DownloadTaskRequest request = new(fileName, [new DownloadFileRequest(destination, [source])]);
        LauncherTaskStage stage = new("下载文件", context => context.DownloadAsync(request, options));
        LauncherTask task = Start($"自定义下载 · {fileName}", [stage], destination);
        App.Logger.Information("Tasks", $"创建自定义下载任务: {fileName}, 来源主机: {source.Host}.");
        return task;
    }

    /// <summary>创建由多个阶段组成的任务, 后续安装与模组操作可复用.</summary>
    internal LauncherTask Start(string title, IReadOnlyList<LauncherTaskStage> stages, string? targetPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (stages.Count == 0)
        {
            throw new ArgumentException("任务至少需要一个阶段.", nameof(stages));
        }

        LauncherTask task = new(title, stages, targetPath);
        Tasks.Insert(0, task);
        _ = RunAsync(task);
        return task;
    }

    internal void CancelOrRemove(LauncherTask task)
    {
        if (task.IsActive)
        {
            task.Cancellation.Cancel();
            foreach (LauncherTaskStage stage in task.Stages.Where(stage => stage.Status == LauncherTaskStatus.Running))
            {
                stage.Status = LauncherTaskStatus.Canceled;
            }
            task.Status = LauncherTaskStatus.Canceled;
            return;
        }

        Tasks.Remove(task);
    }

    private static async Task RunAsync(LauncherTask task)
    {
        try
        {
            task.Status = LauncherTaskStatus.Running;
            foreach (LauncherTaskStage stage in task.Stages)
            {
                task.Cancellation.Token.ThrowIfCancellationRequested();
                stage.Status = LauncherTaskStatus.Running;
                await stage.Execute(new LauncherTaskContext(task, stage));
                task.Cancellation.Token.ThrowIfCancellationRequested();
                stage.Progress = 100;
                stage.Status = LauncherTaskStatus.Completed;
                task.RefreshProgress();
            }

            task.Status = LauncherTaskStatus.Completed;
            LauncherToasts.Success($"任务已完成: {task.Title}");
            App.Logger.Information("Tasks", $"任务完成: {task.Title}.");
            App.Tasks.Tasks.Remove(task);
        }
        catch (OperationCanceledException) when (task.Cancellation.IsCancellationRequested)
        {
            foreach (LauncherTaskStage stage in task.Stages.Where(stage => stage.Status == LauncherTaskStatus.Running))
            {
                stage.Status = LauncherTaskStatus.Canceled;
            }
            task.Status = LauncherTaskStatus.Canceled;
            LauncherToasts.Information($"任务已取消: {task.Title}");
            App.Logger.Information("Tasks", $"任务取消: {task.Title}.");
        }
        catch (Exception error)
        {
            foreach (LauncherTaskStage stage in task.Stages.Where(stage => stage.Status == LauncherTaskStatus.Running))
            {
                stage.Status = LauncherTaskStatus.Failed;
            }
            task.Error = error.GetBaseException().Message;
            task.Status = LauncherTaskStatus.Failed;
            LauncherToasts.Error($"任务失败: {task.Error}");
            App.Logger.Error("Tasks", $"任务失败: {task.Title}.", error);
        }
        finally
        {
            task.SpeedBytesPerSecond = 0;
            task.RemainingFiles = 0;
            task.Cancellation.Dispose();
        }
    }
}
