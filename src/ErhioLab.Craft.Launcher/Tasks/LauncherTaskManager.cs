using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Tasks;

/// <summary>统一管理启动器操作的阶段/进度和取消, 下载器由任务阶段调用.</summary>
internal sealed class LauncherTaskManager
{
    private static readonly HttpClient InstallerHttp = new(new HttpClientHandler
    {
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12
    }) { Timeout = TimeSpan.FromSeconds(35) };
    static LauncherTaskManager() => InstallerHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ELCL/" + Core.CoreInfo.Version);

    public ObservableCollection<LauncherTask> Tasks { get; } = [];

    internal LauncherTask StartMinecraftInstall(MinecraftVersion version, string installedName, string gameDirectory,
        MinecraftManifestSource preferredSource, DownloadOptions options, IReadOnlyList<MinecraftLoaderSelection>? loaders = null)
    {
        MinecraftLoaderSelection[] selections = loaders?.ToArray() ?? [];
        MinecraftVanillaDownloadPlanner.ValidateVersionName(installedName);
        MinecraftLoaderInstaller.ValidateSelections(version.Id, selections);
        gameDirectory = Path.GetFullPath(gameDirectory);
        string baseName = installedName;
        string versionFolder = Path.Combine(gameDirectory, "versions", installedName);
        string clientPath = Path.Combine(versionFolder, installedName + ".jar");
        string versionJsonPath = Path.Combine(versionFolder, installedName + ".json");
        bool clientExisted = File.Exists(clientPath);
        bool versionJsonExisted = File.Exists(versionJsonPath);
        if (versionJsonExisted) throw new InvalidOperationException(LocalizationManager.Get("install.versionExists"));
        if (selections.Length > 0 && Directory.Exists(versionFolder) && Directory.EnumerateFileSystemEntries(versionFolder).Any())
            throw new InvalidOperationException(LocalizationManager.Get("install.versionExists"));
        if (Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, versionFolder, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(LocalizationManager.Get("install.alreadyRunning"));

        string cacheName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version.MetadataUrl.AbsoluteUri)));
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "version-json", cacheName + ".json");
        MinecraftVanillaDownloadPlan? plan = null;
        MinecraftLoaderInstallPlan? loaderPlan = null;
        bool loaderVersionWritten = false;
        var loaderInstaller = new MinecraftLoaderInstaller(InstallerHttp);
        string workDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "loader-install", Guid.NewGuid().ToString("N"));
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
                plan = MinecraftVanillaDownloadPlanner.ParseVersion(json, version.Id, gameDirectory, baseName, preferredSource);
                long supportBytes = plan.Libraries.Sum(LauncherTaskContext.EstimatedSize);
                if (plan.LoggingConfiguration is { } logFile) supportBytes += LauncherTaskContext.EstimatedSize(logFile);
                context.ConfigureStageWeights([
                    new FileInfo(metadataPath).Length,
                    supportBytes,
                    LauncherTaskContext.EstimatedSize(plan.Client),
                    LauncherTaskContext.EstimatedSize(plan.AssetIndex),
                    plan.AssetTotalSize ?? 64L * 1024 * 1024,
                    .. (selections.Length == 0 ? Array.Empty<long>() : new[] { 1024 * 1024, 64L * 1024 * 1024, 1024 * 1024, 1024 * 1024 })
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
                if (selections.Length == 0)
                    await MinecraftVanillaDownloadPlanner.WriteInstalledVersionAsync(plan, gameDirectory, baseName, context.CancellationToken);
                if (selections.Length == 0)
                    await LauncherProfileCompatibility.RegisterVersionAsync(gameDirectory, versionFolder, installedName,
                        installedName, cancellationToken: context.CancellationToken);
            }),
            .. (selections.Length == 0 ? Array.Empty<LauncherTaskStage>() : new[]
            {
                Stage("install.loaderMetadata", async context =>
                {
                    loaderPlan = await loaderInstaller.PrepareAsync(version.Id, installedName, selections, gameDirectory, workDirectory, preferredSource,
                        async (files, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            await Application.Current.Dispatcher.InvokeAsync(() => context.DownloadAsync(new DownloadTaskRequest(installedName, files), downloadOptions)).Task.Unwrap();
                        }, message => App.Logger.Information("Installer", message), context.CancellationToken,
                        baseVersionJsonPath: metadataPath, baseClientJarPath: plan!.Client.DestinationPath);
                }),
                Stage("install.loaderFiles", async context =>
                {
                    var files = loaderPlan!.Files.DistinctBy(file => file.DestinationPath, StringComparer.OrdinalIgnoreCase).ToArray();
                    var missing = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(files, context.CancellationToken);
                    context.ConfigureDownloadPlan(files, missing);
                    if (missing.Count > 0) await context.DownloadAsync(new DownloadTaskRequest(installedName, missing), downloadOptions);
                }),
                Stage("install.loaderProcessors", async context =>
                {
                    string? javaPath = null;
                    if (loaderPlan!.NeedsJava)
                    {
                        if (App.Settings.JavaInstallations.Count == 0) await App.JavaCatalog.InitializeIfEmptyAsync();
                        var baseVersion = await MinecraftLaunchVersion.LoadAsync(gameDirectory, metadataPath, cancellationToken: context.CancellationToken);
                        foreach (var java in App.Settings.JavaInstallations.OrderBy(candidate => candidate.MajorVersion == baseVersion.JavaMajorVersion ? 0 : 1))
                        {
                            if (java.MajorVersion < baseVersion.JavaMajorVersion) continue;
                            if (await JavaDiscovery.ProbeAsync(java.Path, context.CancellationToken) is { } found) { javaPath = found.Path; break; }
                        }
                        if (javaPath is null) throw new InvalidOperationException(LocalizationManager.Get("install.noInstallerJava"));
                    }
                    await loaderInstaller.RunProcessorsAsync(loaderPlan, javaPath, InstallerProcessRunner.RunAsync,
                        new Progress<double>(context.ReportProgress), context.CancellationToken);
                }),
                Stage("install.finalize", async context =>
                {
                    await MinecraftLoaderInstaller.WriteVersionAsync(loaderPlan!, context.CancellationToken);
                    loaderVersionWritten = true;
                    await LauncherProfileCompatibility.RegisterVersionAsync(gameDirectory, versionFolder, installedName, installedName, cancellationToken: context.CancellationToken);
                    CleanupLoaderWorkDirectory(workDirectory);
                })
            })
        ];

        LauncherTask task = Start(string.Format(LocalizationManager.Get("install.title"), installedName), stages, versionFolder,
            async () =>
            {
                if (selections.Length == 0)
                    CleanupVanillaVersionDirectory(versionFolder, clientPath, versionJsonPath, clientExisted, versionJsonExisted);
                else
                {
                    if (loaderVersionWritten) File.Delete(versionJsonPath);
                    if (!clientExisted && File.Exists(clientPath)) File.Delete(clientPath);
                }
                CleanupLoaderWorkDirectory(workDirectory);
                if (loaderPlan is not null)
                {
                    string mods = Path.Combine(versionFolder, "mods");
                    foreach (string file in loaderPlan.VersionFiles)
                        if (File.Exists(file)) File.Delete(file);
                    if (Directory.Exists(mods) && !Directory.EnumerateFileSystemEntries(mods).Any()) Directory.Delete(mods);
                    if (Directory.Exists(versionFolder) && !Directory.EnumerateFileSystemEntries(versionFolder).Any()) Directory.Delete(versionFolder);
                }
                if (Directory.Exists(versionFolder) && !Directory.EnumerateFileSystemEntries(versionFolder).Any()) Directory.Delete(versionFolder);
                if (!versionJsonExisted)
                    await LauncherProfileCompatibility.RemoveVersionAsync(gameDirectory, versionFolder);
            });
        task.LocalizedTitle = () => string.Format(LocalizationManager.Get("install.title"), installedName);
        return task;
    }

    private static void CleanupLoaderWorkDirectory(string path)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "loader-install")) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("安装工作目录超出预期范围。");
        if (Directory.Exists(full)) Directory.Delete(full, true);
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
        Func<Task>? cleanupOnRemove = null, Action? onCompleted = null, bool isGameLaunch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (stages.Count == 0)
        {
            throw new ArgumentException(LocalizationManager.Get("task.stageRequired"), nameof(stages));
        }

        LauncherTask task = new(title, stages, targetPath)
        {
            CleanupOnRemove = cleanupOnRemove, OnCompleted = onCompleted, IsGameLaunch = isGameLaunch
        };
        Tasks.Insert(0, task);
        task.Completion = RunAsync(task);
        return task;
    }

    /// <summary>跟踪一次已启动的游戏; 终止回调绑定具体进程实例, 不按进程名查杀.</summary>
    internal LauncherTask TrackRunningGame(string name, string versionDirectory, int processId,
        Func<LauncherTask, Task<int>> observe, Func<Task> stop, string? logPath = null)
    {
        LauncherTask task = new(name, [], versionDirectory)
        {
            IsGameLaunch = true, IsRunningGame = true, ProcessId = processId, GameLogPath = logPath, StopAsync = stop, Status = LauncherTaskStatus.Running
        };
        Tasks.Insert(0, task);
        App.Logger.Information("Tasks", $"开始跟踪运行中的游戏: {name}, PID={processId}.");
        task.Completion = ObserveRunningGameAsync(task, observe);
        return task;
    }

    private async Task ObserveRunningGameAsync(LauncherTask task, Func<LauncherTask, Task<int>> observe)
    {
        try
        {
            int code = await observe(task);
            if (task.IsStopping || code == 0)
            {
                task.Status = task.IsStopping ? LauncherTaskStatus.Canceled : LauncherTaskStatus.Completed;
                App.Logger.Information("Tasks", $"游戏运行任务结束: {task.Title}, PID={task.ProcessId}, 主动终止={task.IsStopping}, 退出码={code}.");
                if (task.IsStopping) LauncherToasts.Information(string.Format(LocalizationManager.Get("gameTask.terminated"), task.Title));
                Tasks.Remove(task);
            }
            else
            {
                task.Error ??= string.Format(LocalizationManager.Get("gameTask.exitError"), code);
                task.Status = LauncherTaskStatus.Failed;
            }
        }
        catch (Exception error)
        {
            task.Error = error.GetBaseException().Message;
            task.Status = LauncherTaskStatus.Failed;
            App.Logger.Error("Tasks", $"跟踪游戏进程失败: {task.Title}, PID={task.ProcessId}.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("task.failedToast"), task.Error));
        }
        finally
        {
            task.IsStopping = false;
            task.Cancellation.Dispose();
        }
    }

    internal async Task CancelOrRemoveAsync(LauncherTask task)
    {
        if (task.IsRunningGame && task.IsActive)
        {
            if (task.IsStopping || task.StopAsync is null) return;
            task.IsStopping = true;
            task.Error = null;
            App.Logger.Information("Tasks", $"用户终止游戏: {task.Title}, PID={task.ProcessId}.");
            try
            {
                await task.StopAsync();
                await task.Completion;
            }
            catch (Exception error)
            {
                task.IsStopping = false;
                task.Error = string.Format(LocalizationManager.Get("gameTask.terminateFailed"), error.Message);
                App.Logger.Error("Tasks", $"无法终止游戏: {task.Title}, PID={task.ProcessId}.", error);
                LauncherToasts.Error(task.Error);
            }
            return;
        }
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
            if (task.CleanupOnRemove is { } cleanup) await cleanup();
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
                App.Logger.Information("Tasks", $"阶段开始: {task.Title} / {stage.Name}.");
                await stage.Execute(new LauncherTaskContext(task, stage));
                task.Cancellation.Token.ThrowIfCancellationRequested();
                stage.Progress = 100;
                stage.Status = LauncherTaskStatus.Completed;
                task.RefreshProgress();
                App.Logger.Information("Tasks", $"阶段完成: {task.Title} / {stage.Name}.");
            }

            task.Status = LauncherTaskStatus.Completed;
            // 先移交到常驻任务，再移除准备任务，避免任务页在交接时因空队列而关闭。
            task.OnCompleted?.Invoke();
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
