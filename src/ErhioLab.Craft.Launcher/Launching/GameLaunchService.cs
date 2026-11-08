using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.ComponentModel;

using ErhioLab.Craft.Launcher.Accounts;
using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Tasks;

namespace ErhioLab.Craft.Launcher.Launching;

/// <summary>将账户/核心启动计划与 Windows 进程接入任务管理; 每次启动固定一份设置快照.</summary>
internal sealed class GameLaunchService
{
    private static readonly HttpClient MetadataHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HashSet<string> _runningVersions = new(StringComparer.OrdinalIgnoreCase);
    internal bool IsPreparing { get; private set; }
    internal event Action? StateChanged;

    internal async Task StartAsync()
    {
        if (IsPreparing) return;
        LauncherSettings settings = App.Settings;
        AccountEntry? account = App.Accounts.SelectedAccount;
        if (account is null)
        {
            App.Logger.Warning("Launch", "启动检查失败: 未选择账户.");
            await LauncherDialogs.WarningAsync(L("gameLaunch.title"), L("gameLaunch.noAccount"));
            if (Application.Current.MainWindow is MainWindow main) main.OpenAccountManagement();
            return;
        }

        if (settings.SelectedGameFolderPath is not { } gameDirectory || settings.SelectedVersionPath is not { } versionDirectory || !Directory.Exists(versionDirectory))
        {
            App.Logger.Warning("Launch", "启动检查失败: 所选游戏文件夹或版本不存在.");
            await LauncherDialogs.WarningAsync(L("gameLaunch.title"), L("gameLaunch.noVersion"));
            return;
        }

        string jsonPath = Path.Combine(versionDirectory, Path.GetFileName(versionDirectory) + ".json");
        if (!File.Exists(jsonPath)) jsonPath = Directory.EnumerateFiles(versionDirectory, "*.json").FirstOrDefault() ?? "";
        if (!File.Exists(jsonPath))
        {
            App.Logger.Warning("Launch", "启动检查失败: 版本 JSON 不存在.");
            await LauncherDialogs.WarningAsync(L("gameLaunch.title"), L("gameLaunch.noVersion"));
            return;
        }

        if (_runningVersions.Contains(versionDirectory))
        {
            LauncherToasts.Information(L("gameLaunch.alreadyRunning"));
            return;
        }

        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, versionDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            LauncherToasts.Information(L("gameLaunch.installRunning"));
            return;
        }

        IsPreparing = true;
        StateChanged?.Invoke();
        string name = settings.SelectedVersionId ?? Path.GetFileName(versionDirectory);
        string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string runDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "launch", runId);
        string natives = Path.Combine(runDirectory, "natives");
        MinecraftManifestSource source = settings.FileDownloadSource == VersionListSource.OfficialFirst ? MinecraftManifestSource.Official : MinecraftManifestSource.Mirror;
        DownloadOptions downloads = new(settings.DownloadMaxConnections, (long)(settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024));
        MinecraftLaunchVersion? version = null;
        MinecraftLaunchFilePlan? files = null;
        MinecraftLaunchOptions? options = null;
        MinecraftLaunchPlaceholderContext? context = null;
        JavaInstallation? java = null;
        MicrosoftMinecraftSession? session = null;
        RunningGame? running = null;
        string accessToken = "0";
        int memory = 0;

        LauncherTaskStage Stage(string key, Func<LauncherTaskContext, Task> action) => new(L(key), async task =>
        {
            try
            {
                await action(task);
            }
            catch (OperationCanceledException) when (task.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (key == "gameLaunch.account")
            {
                string message = error is MicrosoftMinecraftAuthException { Code: "no_entitlement" } ? L("gameLaunch.noEntitlement") : L("gameLaunch.loginFailed");
                await LauncherDialogs.WarningAsync(L("gameLaunch.title"), message);
                throw;
            }
            catch (Exception error) when (key == "gameLaunch.java" && error is InvalidOperationException)
            {
                await LauncherDialogs.WarningAsync(L("gameLaunch.title"), error.Message);
                throw;
            }
        }) { LocalizedName = () => L(key) };

        try
        {
            LauncherToasts.Information(string.Format(L("gameLaunch.preparing"), name));
            App.Logger.Information("Launch", $"开始启动版本: {name}; 账户类型: {(account.IsMicrosoft ? "Microsoft" : "离线")}.");
            App.Logger.Information("Launch", $"固定版本隔离: 工作目录={versionDirectory}; 共享支持库与资源目录={gameDirectory}.");
            var task = App.Tasks.Start(string.Format(L("gameLaunch.task"), name),
            [
                Stage("gameLaunch.account", async task =>
                {
                    // 启动还包括登录/命令和窗口等待; 固定阶段权重避免下载完后整体提前显示 100%.
                    // 下载阶段内部仍按实际文件字节计算进度.
                    task.ConfigureStageWeights(settings.OptimizeMemoryBeforeLaunch ? [5, 5, 5, 5, 30, 40, 5, 5, 5] : [5, 5, 5, 30, 40, 5, 5, 5]);
                    if (!account.IsMicrosoft) return;
                    using var handler = new HttpClientHandler();
                    if (!settings.VerifyMicrosoftCertificates)
                    {
                        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                        App.Logger.Warning("Launch", "按启动设置关闭本次账户验证请求的 SSL 证书验证; 其他网络客户端不受影响.");
                    }

                    using var http = new HttpClient(handler);
                    http.Timeout = TimeSpan.FromSeconds(30);
                    session = await LauncherMicrosoftSessions.GetAsync(account.Uuid, http, forceRefresh: false, task.CancellationToken);
                    accessToken = session.MinecraftAccessToken;
                }),
                Stage("gameLaunch.version", async task =>
                {
                    version = await MinecraftLaunchVersion.LoadAsync(gameDirectory, jsonPath,
                        (id, token) => Application.Current.Dispatcher.InvokeAsync(() => FetchParentAsync(id, source, downloads, task, token)).Task.Unwrap(), task.CancellationToken);
                    App.Logger.Information("Launch", $"版本元数据已解析: 基础版本={version.GameVersion}, Java 要求={version.JavaMajorVersion}.");
                }),
                .. (settings.OptimizeMemoryBeforeLaunch ? new[] { Stage("memoryOptimization.launchStage", async task =>
                {
                    try { await App.MemoryOptimizer.OptimizeAsync(showNotifications: false, task.CancellationToken); }
                    catch (OperationCanceledException) when (task.CancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        // 可选优化失败不阻止启动, 服务已记录完整异常.
                        LauncherToasts.Information(L("memoryOptimization.launchFailed"));
                    }
                }) } : Array.Empty<LauncherTaskStage>()),
                Stage("gameLaunch.java", async task =>
                {
                    if (App.Settings.JavaInstallations.Count == 0) await App.JavaCatalog.InitializeIfEmptyAsync();
                    foreach (var candidate in App.Settings.JavaInstallations)
                    {
                        task.CancellationToken.ThrowIfCancellationRequested();
                        if (!version!.IsJavaCompatible(candidate.MajorVersion)) continue;
                        var probed = await JavaDiscovery.ProbeAsync(candidate.Path, task.CancellationToken);
                        if (probed is not null && version.IsJavaCompatible(probed.MajorVersion))
                        {
                            java = probed;
                            break;
                        }
                    }

                    if (java is null) throw new InvalidOperationException(string.Format(L("gameLaunch.noJava"), version!.JavaMajorVersion));
                    var snapshot = GameMemoryAllocation.ReadPhysicalMemory();
                    double gib = GameMemoryAllocation.CalculateGiB(snapshot.AvailableGiB, settings.AutoAllocateMemory, settings.CustomMemoryPercent);
                    memory = (int)Math.Floor(Math.Min(gib * 1024, 1024 * 1024));
                    if (java.Architecture.Contains("32", StringComparison.Ordinal) || java.Architecture is "x86" or "i386") memory = Math.Min(memory, 1024);
                    if (memory < 128) throw new InvalidOperationException(L("gameLaunch.lowMemory"));
                    App.Logger.Information("Launch", $"选用 Java: {java.Path}, 版本={java.Version}, 架构={java.Architecture}, 游戏内存={memory} MiB.");
                }),
                Stage("gameLaunch.files", async task =>
                {
                    var environment = MinecraftLaunchEnvironment.Current with { Architecture = JavaArchitecture(java!) };
                    files = MinecraftLaunchFiles.Plan(version!, source, environment);
                    await VerifyAndDownloadAsync(files.Files, downloads, task, name);
                }),
                Stage("gameLaunch.assets", async task =>
                {
                    string indexJson = await File.ReadAllTextAsync(files!.AssetIndex.DestinationPath, task.CancellationToken);
                    var assets = MinecraftVanillaDownloadPlanner.ParseAssets(indexJson, gameDirectory, source);
                    await VerifyAndDownloadAsync(assets, downloads, task, name);
                    await Task.Run(() => MinecraftLaunchFiles.MaterializeLegacyAssetsAsync(indexJson, gameDirectory, versionDirectory, files.AssetIndexId, task.CancellationToken), task.CancellationToken);
                }),
                Stage("gameLaunch.prepare", async task =>
                {
                    await Task.Run(() => MinecraftLaunchFiles.ExtractNativesAsync(files!, natives, task.CancellationToken), task.CancellationToken);
                    string patchDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "launch-patches");
                    bool customAgent = MinecraftLaunchBuilder.SplitArguments(settings.JvmArguments).Any(value => value.StartsWith("-javaagent:", StringComparison.Ordinal));
                    string? wrapper = !settings.DisableJavaLaunchWrapper && !customAgent
                        ? await LaunchPatches.ExtractAsync("JavaWrapper.jar", patchDirectory, task.CancellationToken)
                        : null;
                    using var metadata = JsonDocument.Parse(version!.Json);
                    bool lwjgl341 = metadata.RootElement.GetProperty("libraries").EnumerateArray()
                        .Any(value => value.TryGetProperty("name", out var coordinate) && coordinate.GetString() == "org.lwjgl:lwjgl:3.4.1");
                    string? agent = !settings.DisableLwjglUnsafeAgent && lwjgl341
                        ? await LaunchPatches.ExtractAsync("LwjglUnsafeAgent.jar", patchDirectory, task.CancellationToken)
                        : null;
                    var info = await Task.Run(() => LocalMinecraftVersionScanner.Scan(gameDirectory).FirstOrDefault(value => string.Equals(value.FolderPath, versionDirectory, StringComparison.OrdinalIgnoreCase)), task.CancellationToken);
                    context = LaunchPlaceholderPreview.Create(info, settings, account) with
                    {
                        GameVersion = version.GameVersion, VersionName = name,
                        PlayerName = session?.UserName ?? account.UserName, PlayerUuid = account.Uuid,
                        LoginType = L(account.IsMicrosoft ? "launch.microsoftAccount" : "account.offlineShort")
                    };
                    (int? width, int? height) = WindowDimensions(settings);
                    options = new()
                    {
                        Java = java!, Context = context, WorkingDirectory = versionDirectory, NativesDirectory = natives,
                        AccessToken = accessToken, IsMicrosoftAccount = account.IsMicrosoft, ClientId = App.Services.MicrosoftClientId,
                        MemoryMiB = memory, GarbageCollector = (MinecraftGarbageCollector)settings.GarbageCollector,
                        JvmArguments = settings.JvmArguments, GameArguments = settings.GameArguments,
                        WindowWidth = width, WindowHeight = height, Fullscreen = settings.WindowMode == GameWindowMode.Fullscreen,
                        JavaWrapperPath = wrapper, LwjglAgentPath = agent,
                        Environment = MinecraftLaunchEnvironment.Current with { Architecture = JavaArchitecture(java!) }
                    };
                    context = MinecraftLaunchBuilder.CreateContext(version, files!, options);
                    // 在执行用户命令前验证所有参数, 避免参数错误时仍产生命令副作用.
                    _ = MinecraftLaunchBuilder.CreateStartInfo(version, files!, options);
                    if (MinecraftLaunchPlaceholders.GetUnresolvedTokens(MinecraftLaunchPlaceholders.Expand(settings.GameWindowTitle, context)).Count > 0)
                        throw new InvalidOperationException(L("gameLaunch.unknownTitle"));
                    App.Logger.Information("Launch", $"启动参数已生成: 窗口模式={settings.WindowMode}, GC={settings.GarbageCollector}, JLW={wrapper is not null}, LUA={agent is not null}.");
                }),
                Stage("gameLaunch.command", task => PreLaunchCommandRunner.RunAsync(settings, context!, task.CancellationToken)),
                Stage("gameLaunch.process", async task =>
                {
                    task.CancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        GameWindowController.SetGpuPreference(java!.Path, settings.PreferHighPerformanceGpu);
                        App.Logger.Information("Launch", $"Java 显卡偏好已应用: 高性能={settings.PreferHighPerformanceGpu}.");
                    }
                    catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                    {
                        App.Logger.Error("Launch", "无法设置 Java 显卡偏好, 将继续启动.", error);
                        LauncherToasts.Information(L("gameLaunch.gpuFailed"));
                    }

                    var start = MinecraftLaunchBuilder.CreateStartInfo(version!, files!, options!);
                    string log = Path.Combine(AppContext.BaseDirectory, "ELCL", "logs", "game", runId + ".log");
                    running = RunningGame.Start(start, log, accessToken, session?.MicrosoftRefreshToken, settings.GamePriority);
                    App.Logger.Information("Launch", $"游戏进程已创建: PID={running.Process.Id}, 日志={log}.");
                    _runningVersions.Add(versionDirectory);
                    while (!running.Process.HasExited)
                    {
                        task.CancellationToken.ThrowIfCancellationRequested();
                        nint window = GameWindowController.FindWindow(running.Process);
                        if (window != 0)
                        {
                            GameWindowController.ApplyWindow(window, settings.GameWindowTitle, context!, settings.WindowMode == GameWindowMode.Maximized);
                            App.Logger.Information("Launch", $"游戏窗口已出现: PID={running.Process.Id}.");
                            await LauncherProfileCompatibility.RegisterVersionAsync(gameDirectory, versionDirectory,
                                Path.GetFileName(versionDirectory), name, markUsed: true, task.CancellationToken);
                            return;
                        }

                        await Task.Delay(250, task.CancellationToken);
                    }

                    await running.Completion;
                    throw new InvalidOperationException(string.Format(L("gameLaunch.exitedEarly"), running.Process.ExitCode, log));
                })
            ], versionDirectory, onCompleted: () =>
            {
                var game = running ?? throw new InvalidOperationException("启动完成时没有可跟踪的游戏进程.");
                App.Tasks.TrackRunningGame(name, versionDirectory, game.Process.Id,
                    runningTask => WatchGameAsync(game, versionDirectory, runDirectory, name, settings.GameWindowTitle, context!, runningTask),
                    game.TerminateAsync);
                running = null;
            });
            task.LocalizedTitle = () => string.Format(L("gameLaunch.task"), name);
            await task.Completion;
        }
        catch (Exception error)
        {
            App.Logger.Error("Launch", "无法创建启动任务.", error);
            LauncherToasts.Error(L("gameLaunch.startFailed"));
        }
        finally
        {
            if (running is not null)
            {
                running.Kill();
                await running.Completion;
                running.Dispose();
                _runningVersions.Remove(versionDirectory);
            }

            if (!_runningVersions.Contains(versionDirectory)) CleanupRunDirectory(runDirectory);
            IsPreparing = false;
            StateChanged?.Invoke();
        }
    }

    private async Task<int> WatchGameAsync(RunningGame running, string versionDirectory, string runDirectory, string name,
        string title, MinecraftLaunchPlaceholderContext context, LauncherTask task)
    {
        try
        {
            bool titleFailed = false;
            while (!running.Completion.IsCompleted)
            {
                if (!titleFailed && !running.Process.HasExited && !string.IsNullOrWhiteSpace(title))
                {
                    try
                    {
                        nint window = GameWindowController.FindWindow(running.Process);
                        if (window != 0) GameWindowController.ApplyWindow(window, title, context, maximize: false);
                    }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception)
                    {
                        // 窗口刷新失败不应丢失仍在运行的进程及其终止入口.
                        titleFailed = true;
                        App.Logger.Error("Launch", "无法继续更新游戏窗口标题, 将保留进程跟踪.", error);
                    }
                }

                await Task.WhenAny(running.Completion, Task.Delay(1000));
            }

            int code = await running.Completion;
            App.Logger.Information("Launch", $"游戏退出: {name}, PID={running.Process.Id}, 退出码={code}, 日志={running.LogPath}.");
            if (!task.IsStopping)
            {
                if (code == 0) LauncherToasts.Information(string.Format(L("gameLaunch.exited"), name));
                else
                {
                    task.Error = string.Format(L("gameLaunch.crashed"), name, code, running.LogPath);
                    LauncherToasts.Error(task.Error);
                }
            }
            return code;
        }
        catch (Exception error)
        {
            App.Logger.Error("Launch", "监视游戏进程时发生错误.", error);
            await running.Completion;
            throw;
        }
        finally
        {
            _runningVersions.Remove(versionDirectory);
            running.Dispose();
            CleanupRunDirectory(runDirectory);
            StateChanged?.Invoke();
        }
    }

    private static async Task VerifyAndDownloadAsync(IReadOnlyList<DownloadFileRequest> all, DownloadOptions options,
        LauncherTaskContext task, string name)
    {
        var missing = await Task.Run(() => MinecraftVanillaDownloadPlanner.MissingFilesAsync(all, task.CancellationToken), task.CancellationToken);
        if (missing.FirstOrDefault(file => file.Sources.Count == 0) is { } local)
            throw new FileNotFoundException(string.Format(L("gameLaunch.localLibraryMissing"), local.DestinationPath));
        task.ConfigureDownloadPlan(all, missing, preserveStageWeight: true);
        App.Logger.Information("Launch", $"文件完整性检查完成: 总数={all.Count}, 需补全={missing.Count}.");
        if (missing.Count > 0) await task.DownloadAsync(new DownloadTaskRequest(name, missing), options);
    }

    private static async Task<string> FetchParentAsync(string id, MinecraftManifestSource source, DownloadOptions options,
        LauncherTaskContext task, CancellationToken cancellationToken)
    {
        var catalog = await new MinecraftVersionCatalogClient(MetadataHttp).GetCatalogAsync(source, cancellationToken);
        var version = catalog.Versions.FirstOrDefault(value => value.Id == id)
                      ?? throw new FileNotFoundException(string.Format(L("gameLaunch.parentMissing"), id));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version.MetadataUrl.AbsoluteUri)));
        string path = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "version-json", hash + ".json");
        await VerifyAndDownloadAsync([MinecraftVanillaDownloadPlanner.VersionMetadata(version, path, source)], options, task, id);
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static (int? Width, int? Height) WindowDimensions(LauncherSettings settings)
    {
        if (settings.WindowMode == GameWindowMode.Custom) return (settings.GameWindowWidth, settings.GameWindowHeight);
        if (settings.WindowMode == GameWindowMode.MatchLauncher && Application.Current.MainWindow is { } window)
        {
            var source = PresentationSource.FromVisual(window);
            var transform = source?.CompositionTarget?.TransformToDevice;
            return (Math.Max(1, (int)Math.Round(window.ActualWidth * (transform?.M11 ?? 1))), Math.Max(1, (int)Math.Round(window.ActualHeight * (transform?.M22 ?? 1))));
        }

        return (null, null);
    }

    private static string JavaArchitecture(JavaInstallation java) => java.Architecture.Contains("aarch64", StringComparison.OrdinalIgnoreCase)
                                                                     || java.Architecture.Contains("arm64", StringComparison.OrdinalIgnoreCase) ? "aarch64"
        : java.Architecture.Contains("64", StringComparison.Ordinal) ? "amd64" : "x86";

    private static void CleanupRunDirectory(string directory)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "launch")) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(directory);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("运行缓存清理路径越界.");
        try
        {
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Launch", "无法清理本次运行的本地库缓存.", error);
        }
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
