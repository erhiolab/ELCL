using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Tasks;

/// <summary>统一管理启动器操作的阶段、进度和取消, 下载器由任务阶段调用.</summary>
internal sealed class LauncherTaskManager
{
    public ObservableCollection<LauncherTask> Tasks { get; } = [];

    internal LauncherTask StartVanillaInstall(MinecraftVersion version, string installedName, string gameDirectory,
        MinecraftManifestSource preferredSource, DownloadOptions options)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        string versionFolder = Path.Combine(gameDirectory, "versions", installedName);
        string clientPath = Path.Combine(versionFolder, installedName + ".jar");
        string versionJsonPath = Path.Combine(versionFolder, installedName + ".json");
        bool clientExisted = File.Exists(clientPath);
        bool versionJsonExisted = File.Exists(versionJsonPath);
        if (Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, versionFolder, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(LocalizationManager.Get("install.alreadyRunning"));

        string cacheName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version.MetadataUrl.AbsoluteUri)));
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "version-json", cacheName + ".json");
        MinecraftVanillaDownloadPlan? plan = null;
        DownloadOptions downloadOptions = options;
        LauncherTaskStage Stage(string key, Func<LauncherTaskContext, Task> execute) =>
            new(LocalizationManager.Get(key), execute) { LocalizedName = () => LocalizationManager.Get(key) };

        IReadOnlyList<LauncherTaskStage> stages =
        [
            Stage("install.versionJson", async context =>
            {
                DownloadFileRequest metadata = MinecraftVanillaDownloadPlanner.VersionMetadata(version, metadataPath, preferredSource);
                if (version.Sha1 is null || (await MinecraftVanillaDownloadPlanner.MissingFilesAsync([metadata], context.CancellationToken)).Count > 0)
                    await context.DownloadAsync(new DownloadTaskRequest(version.Id, [metadata]), downloadOptions);
                string json = await File.ReadAllTextAsync(metadataPath, context.CancellationToken);
                plan = MinecraftVanillaDownloadPlanner.ParseVersion(json, version.Id, gameDirectory, installedName, preferredSource);
                long supportBytes = plan.Libraries.Sum(LauncherTaskContext.EstimatedSize);
                if (plan.LoggingConfiguration is { } logFile) supportBytes += LauncherTaskContext.EstimatedSize(logFile);
                context.ConfigureStageWeights([
                    new FileInfo(metadataPath).Length,
                    supportBytes,
                    LauncherTaskContext.EstimatedSize(plan.Client),
                    LauncherTaskContext.EstimatedSize(plan.AssetIndex),
                    plan.AssetTotalSize ?? 64L * 1024 * 1024
                ]);
            }),
            Stage("install.libraries", async context =>
            {
                DownloadFileRequest[] supportFiles = plan!.LoggingConfiguration is { } logging
                    ? [.. plan.Libraries, logging] : [.. plan.Libraries];
                IReadOnlyList<DownloadFileRequest> files = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(supportFiles, context.CancellationToken);
                context.ConfigureDownloadPlan(supportFiles, files);
                if (files.Count > 0) await context.DownloadAsync(new DownloadTaskRequest(installedName, files), downloadOptions);
            }),
            Stage("install.client", async context =>
            {
                DownloadFileRequest[] allFiles = [plan!.Client];
                IReadOnlyList<DownloadFileRequest> files = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(allFiles, context.CancellationToken);
                context.ConfigureDownloadPlan(allFiles, files);
                if (files.Count > 0) await context.DownloadAsync(new DownloadTaskRequest(installedName, files), downloadOptions);
            }),
            Stage("install.assetIndex", async context =>
            {
                DownloadFileRequest[] allFiles = [plan!.AssetIndex];
                IReadOnlyList<DownloadFileRequest> files = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(allFiles, context.CancellationToken);
                context.ConfigureDownloadPlan(allFiles, files);
                if (files.Count > 0) await context.DownloadAsync(new DownloadTaskRequest(installedName, files), downloadOptions);
            }),
            Stage("install.assets", async context =>
            {
                string indexJson = await File.ReadAllTextAsync(plan!.AssetIndex.DestinationPath, context.CancellationToken);
                IReadOnlyList<DownloadFileRequest> assets = MinecraftVanillaDownloadPlanner.ParseAssets(indexJson, gameDirectory, preferredSource);
                IReadOnlyList<DownloadFileRequest> files = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(assets, context.CancellationToken);
                context.ConfigureDownloadPlan(assets, files);
                if (files.Count > 0) await context.DownloadAsync(new DownloadTaskRequest(installedName, files), downloadOptions);
                context.CancellationToken.ThrowIfCancellationRequested();
                await MinecraftVanillaDownloadPlanner.WriteInstalledVersionAsync(plan, gameDirectory, installedName, context.CancellationToken);
            })
        ];

        LauncherTask task = Start(string.Format(LocalizationManager.Get("install.title"), installedName), stages, versionFolder,
            () => CleanupVanillaVersionDirectory(versionFolder, clientPath, versionJsonPath, clientExisted, versionJsonExisted));
        task.LocalizedTitle = () => string.Format(LocalizationManager.Get("install.title"), installedName);
        return task;
    }

    /// <summary>创建一个可显示在任务管理页的自定义文件下载任务.</summary>
    internal LauncherTask StartCustomDownload(string destination, Uri source, DownloadOptions options)
    {
        destination = Path.GetFullPath(destination);
        if (Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, destination, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(LocalizationManager.Get("task.alreadyDownloading"));
        }

        string fileName = Path.GetFileName(destination);
        DownloadTaskRequest request = new(fileName, [new DownloadFileRequest(destination, [source])]);
        LauncherTaskStage stage = new(LocalizationManager.Get("task.downloadFile"), context => context.DownloadAsync(request, options))
        {
            LocalizedName = () => LocalizationManager.Get("task.downloadFile")
        };
        LauncherTask task = Start(string.Format(LocalizationManager.Get("task.customDownload"), fileName), [stage], destination);
        task.LocalizedTitle = () => string.Format(LocalizationManager.Get("task.customDownload"), fileName);
        App.Logger.Information("Tasks", $"创建自定义下载任务: {fileName}, 来源主机: {source.Host}.");
        return task;
    }

    /// <summary>创建由多个阶段组成的任务, 后续安装与模组操作可复用.</summary>
    internal LauncherTask Start(string title, IReadOnlyList<LauncherTaskStage> stages, string? targetPath = null,
        Action? cleanupOnRemove = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (stages.Count == 0)
        {
            throw new ArgumentException(LocalizationManager.Get("task.stageRequired"), nameof(stages));
        }

        LauncherTask task = new(title, stages, targetPath) { CleanupOnRemove = cleanupOnRemove };
        Tasks.Insert(0, task);
        task.Completion = RunAsync(task);
        return task;
    }

    internal async Task CancelOrRemoveAsync(LauncherTask task)
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

        await task.Completion;
        if (!Tasks.Contains(task)) return;
        try
        {
            task.CleanupOnRemove?.Invoke();
            Tasks.Remove(task);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Tasks", $"移除任务时无法清理文件: {task.Title}.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("task.cleanupFailed"), error.Message));
        }
    }

    private static void CleanupVanillaVersionDirectory(string versionFolder, string clientPath, string versionJsonPath,
        bool clientExisted, bool versionJsonExisted)
    {
        if (!Directory.Exists(versionFolder)) return;
        if (!clientExisted) File.Delete(clientPath);
        if (!versionJsonExisted) File.Delete(versionJsonPath);
        if (!Directory.EnumerateFileSystemEntries(versionFolder).Any()) Directory.Delete(versionFolder);
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
            LauncherToasts.Success(string.Format(LocalizationManager.Get("task.completedToast"), task.Title));
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
            LauncherToasts.Information(string.Format(LocalizationManager.Get("task.canceledToast"), task.Title));
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
            LauncherToasts.Error(string.Format(LocalizationManager.Get("task.failedToast"), task.Error));
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
