using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Launching;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Versions;

namespace ErhioLab.Craft.Launcher.Tasks;

internal sealed record MinecraftInstallOperation(IReadOnlyList<LauncherTaskStage> Stages, string VersionFolder, Func<Task> Cleanup);

/// <summary>统一管理启动器操作的阶段/进度和取消, 下载器由任务阶段调用.</summary>
internal sealed class LauncherTaskManager
{
    private static readonly HttpClient InstallerHttp = new(new HttpClientHandler
    {
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12
    }) { Timeout = Timeout.InfiniteTimeSpan };
    static LauncherTaskManager() => InstallerHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ELCL/" + Core.CoreInfo.Version);

    public ObservableCollection<LauncherTask> Tasks { get; } = [];

    internal LauncherTask StartMinecraftInstall(MinecraftVersion version, string installedName, string gameDirectory,
        MinecraftManifestSource preferredSource, DownloadOptions options, IReadOnlyList<MinecraftLoaderSelection>? loaders = null)
    {
        var operation = CreateMinecraftInstall(version, installedName, gameDirectory, preferredSource, options, loaders);
        LauncherTask task = Start(string.Format(LocalizationManager.Get("install.title"), installedName), operation.Stages,
            operation.VersionFolder, cleanupOnFailure: operation.Cleanup);
        task.LocalizedTitle = () => string.Format(LocalizationManager.Get("install.title"), installedName);
        return task;
    }

    internal MinecraftInstallOperation CreateMinecraftInstall(MinecraftVersion version, string installedName, string gameDirectory,
        MinecraftManifestSource preferredSource, DownloadOptions options, IReadOnlyList<MinecraftLoaderSelection>? loaders = null,
        LauncherTask? owner = null, VersionLoaderEdit? edit = null)
    {
        MinecraftLoaderSelection[] selections = loaders?.ToArray() ?? [];
        MinecraftVanillaDownloadPlanner.ValidateVersionName(installedName);
        MinecraftLoaderInstaller.ValidateSelections(version.Id, selections);
        gameDirectory = Path.GetFullPath(gameDirectory);
        string baseName = installedName;
        string versionFolder = edit?.StagingDirectory ?? Path.Combine(gameDirectory, "versions", installedName);
        string clientPath = Path.Combine(versionFolder, installedName + ".jar");
        string versionJsonPath = Path.Combine(versionFolder, installedName + ".json");
        ValidateInstallDirectory(versionFolder);
        if (File.Exists(versionJsonPath)) throw new InvalidOperationException(LocalizationManager.Get("install.versionExists"));
        if (selections.Length > 0 && Directory.Exists(versionFolder) && Directory.EnumerateFileSystemEntries(versionFolder).Any())
            throw new InvalidOperationException(LocalizationManager.Get("install.versionExists"));
        if (Tasks.Any(task => task != owner && task.IsActive && string.Equals(task.TargetPath, versionFolder, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(LocalizationManager.Get("install.alreadyRunning"));

        string metadataPath = LauncherCache.Shared.VersionJson(version.MetadataUrl);
        MinecraftVanillaDownloadPlan? plan = null;
        MinecraftLoaderInstallPlan? loaderPlan = null;
        bool folderCreated = false;
        bool clientAdded = false;
        bool versionWritten = false;
        var loaderInstaller = new MinecraftLoaderInstaller(InstallerHttp, options) { VersionOutputDirectory = edit?.StagingDirectory };
        string workDirectory = LauncherCache.Shared.Workspace("loader-install");
        DownloadOptions downloadOptions = options;
        LauncherTaskStage Stage(string key, Func<LauncherTaskContext, Task> execute) =>
            new(LocalizationManager.Get(key), execute) { LocalizedName = () => LocalizationManager.Get(key) };

        IReadOnlyList<LauncherTaskStage> stages =
        [
            Stage("install.versionJson", async context =>
            {
                App.Logger.Information("Installer", $"准备安装版本: {installedName}, 文件下载源优先: {preferredSource}.");
                LauncherCache.Shared.MarkMetadataUsed(metadataPath);
                DownloadFileRequest metadata = MinecraftVanillaDownloadPlanner.VersionMetadata(version, metadataPath, preferredSource);
                if (version.Sha1 is null || (await MinecraftVanillaDownloadPlanner.MissingFilesAsync([metadata], context.CancellationToken)).Count > 0)
                    await context.DownloadAsync(new DownloadTaskRequest(version.Id, [metadata]), downloadOptions);
                string json = await File.ReadAllTextAsync(metadataPath, context.CancellationToken);
                plan = MinecraftVanillaDownloadPlanner.ParseVersion(json, version.Id, gameDirectory, baseName, preferredSource);
                if (edit is not null) plan = plan with { Client = plan.Client with { DestinationPath = clientPath } };
                long supportBytes = plan.Libraries.Sum(LauncherTaskContext.EstimatedSize);
                if (plan.LoggingConfiguration is { } logFile) supportBytes += LauncherTaskContext.EstimatedSize(logFile);
                context.ConfigureStageWeights([
                    new FileInfo(metadataPath).Length,
                    supportBytes,
                    LauncherTaskContext.EstimatedSize(plan.Client),
                    LauncherTaskContext.EstimatedSize(plan.AssetIndex),
                    plan.AssetTotalSize ?? 64L * 1024 * 1024,
                    .. (selections.Length == 0 ? Array.Empty<long>() : new[] { 1024 * 1024, 64L * 1024 * 1024, 1024 * 1024, 1024 * 1024 })
                ], fromCurrentStage: true);
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
                folderCreated = ReserveVersionFolder(versionFolder);
                if (edit is not null && !folderCreated) throw new IOException(LocalizationManager.Get("install.unsafeDirectory"));
                if (File.Exists(versionJsonPath)) throw new InvalidOperationException(LocalizationManager.Get("install.versionExists"));
                bool clientExisted = File.Exists(clientPath);
                if (edit is not null && !CacheFileTree.IsLinked(edit.ClientPath) && plan!.Client.ExpectedSha1 is not null)
                {
                    var installedClient = plan.Client with { DestinationPath = edit.ClientPath };
                    if ((await MinecraftVanillaDownloadPlanner.MissingFilesAsync([installedClient], context.CancellationToken)).Count == 0)
                        await Task.Run(() => File.Copy(edit.ClientPath, clientPath, false), context.CancellationToken);
                }
                DownloadFileRequest[] allFiles = [plan!.Client];
                IReadOnlyList<DownloadFileRequest> files = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(allFiles, context.CancellationToken);
                if (files.Count > 0)
                    files = [await LauncherDownloadPolicy.FindClientCandidatesAsync(plan.Client, gameDirectory, version.Id, options, context.CancellationToken)];
                context.ConfigureDownloadPlan(allFiles, files);
                if (files.Count > 0) await context.DownloadAsync(new DownloadTaskRequest(installedName, files), downloadOptions);
                clientAdded = !clientExisted && files.Count > 0;
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
                {
                    ValidateInstallDirectory(versionFolder);
                    if (File.Exists(versionJsonPath)) throw new InvalidOperationException(LocalizationManager.Get("install.versionExists"));
                    if (edit is null)
                        await MinecraftVanillaDownloadPlanner.WriteInstalledVersionAsync(plan, gameDirectory, baseName, context.CancellationToken);
                    else
                    {
                        var json = JsonNode.Parse(plan.VersionJson)!.AsObject();
                        json["id"] = installedName;
                        json["clientVersion"] = version.Id;
                        await File.WriteAllTextAsync(versionJsonPath, json.ToJsonString(), context.CancellationToken);
                    }
                    versionWritten = true;
                    if (edit is null)
                        await LauncherProfileCompatibility.RegisterVersionAsync(gameDirectory, versionFolder, installedName,
                            installedName, cancellationToken: context.CancellationToken);
                }
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
                        if (App.JavaCatalog.Installations.Count == 0) await App.JavaCatalog.InitializeIfEmptyAsync();
                        var baseVersion = await MinecraftLaunchVersion.LoadAsync(gameDirectory, metadataPath, cancellationToken: context.CancellationToken);
                        foreach (var java in App.JavaCatalog.Installations.OrderBy(candidate => candidate.MajorVersion == baseVersion.JavaMajorVersion ? 0 : 1))
                        {
                            if (java.MajorVersion < baseVersion.JavaMajorVersion) continue;
                            if (await JavaDiscovery.ProbeAsync(java.Path, context.CancellationToken) is { } found) { javaPath = found.Path; break; }
                        }
                        if (javaPath is null) throw new InvalidOperationException(LocalizationManager.Get("install.noInstallerJava"));
                        context.SetJavaExecutablePath(javaPath);
                    }
                    await loaderInstaller.RunProcessorsAsync(loaderPlan, javaPath, InstallerProcessRunner.RunAsync,
                        new Progress<double>(context.ReportProgress), context.CancellationToken);
                }),
                Stage("install.finalize", async context =>
                {
                    await MinecraftLoaderInstaller.WriteVersionAsync(loaderPlan!, context.CancellationToken);
                    versionWritten = true;
                    string? managedMod = loaderPlan!.VersionFiles.FirstOrDefault(path =>
                        VersionManagementService.SamePath(Path.GetDirectoryName(path), Path.Combine(versionFolder, "mods")));
                    await VersionLoaderMetadata.StampAsync(versionJsonPath, selections, managedMod, context.CancellationToken);
                    if (edit is null)
                        await LauncherProfileCompatibility.RegisterVersionAsync(gameDirectory, versionFolder, installedName, installedName, cancellationToken: context.CancellationToken);
                    CleanupLoaderWorkDirectory(workDirectory);
                })
            }),
            .. (edit is null ? [] : new[]
            {
                Stage("versionEdit.apply", async context =>
                {
                    await edit.ApplyAsync(versionJsonPath, clientPath, context.CancellationToken);
                    edit.Cleanup();
                })
            })
        ];

        return new MinecraftInstallOperation(stages, versionFolder,
            async () =>
            {
                try
                {
                    if (edit is not null)
                    {
                        if (folderCreated) edit.Cleanup();
                        return;
                    }
                    if (folderCreated || clientAdded || versionWritten || loaderPlan?.VersionFiles.Count > 0)
                    {
                        ValidateInstallDirectory(versionFolder);
                        string mods = Path.Combine(versionFolder, "mods");
                        if (versionWritten) DeleteInstallFile(versionJsonPath, versionFolder, mods);
                        if (clientAdded) DeleteInstallFile(clientPath, versionFolder, mods);
                        if (loaderPlan is not null)
                            foreach (string file in loaderPlan.VersionFiles)
                                DeleteInstallFile(file, versionFolder, mods);
                        if (folderCreated)
                        {
                            if (loaderPlan is not null) DeleteEmptyInstallDirectory(mods);
                            DeleteEmptyInstallDirectory(versionFolder);
                        }
                    }

                    if (versionWritten)
                        await LauncherProfileCompatibility.RemoveVersionAsync(gameDirectory, versionFolder);
                }
                finally
                {
                    CleanupLoaderWorkDirectory(workDirectory);
                }
            });
    }

    private static void ValidateInstallDirectory(string versionFolder)
    {
        ValidatePlainDirectory(Path.GetDirectoryName(versionFolder)!);
        ValidatePlainDirectory(versionFolder);
    }

    private static bool ReserveVersionFolder(string versionFolder)
    {
        ValidateInstallDirectory(versionFolder);
        if (Directory.Exists(versionFolder)) return false;
        string root = Path.GetDirectoryName(versionFolder)!;
        Directory.CreateDirectory(root);
        string reservation = Path.Combine(root, ".elcl-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reservation);
        try { Directory.Move(reservation, versionFolder); }
        finally { if (Directory.Exists(reservation)) Directory.Delete(reservation); }
        return true;
    }

    private static void ValidatePlainDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new IOException(LocalizationManager.Get("install.unsafeDirectory"));
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static void DeleteInstallFile(string file, string versionFolder, string mods)
    {
        string full = Path.GetFullPath(file);
        string parent = Path.GetDirectoryName(full)!;
        if (!string.Equals(parent, versionFolder, StringComparison.OrdinalIgnoreCase) && !string.Equals(parent, mods, StringComparison.OrdinalIgnoreCase))
            throw new IOException("安装文件超出预期范围.");
        ValidatePlainDirectory(parent);
        if (Directory.Exists(parent)) File.Delete(full);
    }

    private static void DeleteEmptyInstallDirectory(string path)
    {
        ValidatePlainDirectory(path);
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }

    private static void CleanupLoaderWorkDirectory(string path) => LauncherCache.Shared.RemoveWorkspace(path,
        (entry, error) => App.Logger.Error("Cache", $"无法清理加载器安装缓存: {entry}.", error));

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

    /// <summary>创建由多个阶段组成的任务, 后续安装与 Mods 操作可复用.</summary>
    internal LauncherTask Start(string title, IReadOnlyList<LauncherTaskStage> stages, string? targetPath = null,
        Func<Task>? cleanupOnRemove = null, Action? onCompleted = null, bool isGameLaunch = false, Func<Task>? cleanupOnFailure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (stages.Count == 0)
        {
            throw new ArgumentException(LocalizationManager.Get("task.stageRequired"), nameof(stages));
        }

        LauncherTask task = new(title, stages, targetPath)
        {
            CleanupOnRemove = cleanupOnRemove, CleanupOnFailure = cleanupOnFailure, OnCompleted = onCompleted, IsGameLaunch = isGameLaunch
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
            if (task.IsStopping) return;
            task.IsStopping = true;
            task.Cancellation.Cancel();
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

    private static async Task CleanupFailedTaskAsync(LauncherTask task)
    {
        if (task.CleanupOnFailure is not { } cleanup) return;
        try
        {
            await cleanup();
            App.Logger.Information("Tasks", $"安装失败或取消后的清理已完成: {task.TargetPath}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.Logger.Error("Tasks", $"安装失败或取消后无法清理: {task.TargetPath}.", error);
            string message = string.Format(LocalizationManager.Get("task.cleanupFailed"), error.Message);
            task.Error = string.IsNullOrEmpty(task.Error) ? message : task.Error + Environment.NewLine + message;
            LauncherToasts.Error(message);
        }
    }

    private static async Task RunAsync(LauncherTask task)
    {
        try
        {
            task.Status = LauncherTaskStatus.Running;
            for (int index = 0; index < task.Stages.Count; index++)
            {
                LauncherTaskStage stage = task.Stages[index];
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
            // 先移交到常驻任务, 再移除准备任务, 避免任务页在交接时因空队列而关闭.
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
            await CleanupFailedTaskAsync(task);
            task.IsStopping = false;
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
            await CleanupFailedTaskAsync(task);
            task.IsStopping = false;
            task.Status = LauncherTaskStatus.Failed;
            if (error is GameProcessExitException exited)
                GameCrashDialog.Queue(exited.Report);
            else
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
