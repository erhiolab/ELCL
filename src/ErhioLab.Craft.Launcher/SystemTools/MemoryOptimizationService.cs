using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>两个优化入口共用一次后台操作, 避免重复扫描; 自动优化失败不会阻断游戏启动.</summary>
internal sealed class MemoryOptimizationService
{
    private readonly Func<IReadOnlyCollection<int>, CancellationToken, MemoryOptimizationResult> _optimize;
    private Task<MemoryOptimizationResult>? _active;

    internal MemoryOptimizationService(Func<IReadOnlyCollection<int>, CancellationToken, MemoryOptimizationResult>? optimize = null)
    {
        _optimize = optimize ?? ((excluded, token) => WindowsMemoryOptimizer.Optimize(excluded, token));
    }

    internal bool IsOptimizing => _active is { IsCompleted: false };
    internal event Action? Changed;

    internal Task<MemoryOptimizationResult> OptimizeAsync(bool showNotifications, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_active is { IsCompleted: false } active)
        {
            App.Logger.Information("Memory", "已有内存优化正在进行, 本次等待同一操作完成.");
            return active.WaitAsync(cancellationToken);
        }

        var completion = new TaskCompletionSource<MemoryOptimizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _active = completion.Task;
        Changed?.Invoke();
        _ = ExecuteAsync(completion, showNotifications, cancellationToken);
        return completion.Task;
    }

    private async Task ExecuteAsync(TaskCompletionSource<MemoryOptimizationResult> completion, bool showNotifications,
        CancellationToken cancellationToken)
    {
        try
        {
            // 不整理本启动器与它正在跟踪的游戏, 避免手动优化干扰正在运行的游戏.
            int[] excluded = App.Tasks.Tasks.Where(task => task.IsRunningGame && task.IsActive && task.ProcessId.HasValue).Select(task => task.ProcessId!.Value).ToArray();
            App.Logger.Information("Memory", $"开始内存优化: 工作集整理, 排除本启动器/前台进程及 {excluded.Length} 个运行中的游戏进程.");
            if (showNotifications) LauncherToasts.Information(LocalizationManager.Get("memoryOptimization.started"));
            MemoryOptimizationResult result = await Task.Run(() => _optimize(excluded, cancellationToken), cancellationToken);
            MemoryUnitSystem unit = App.Settings.MemoryUnit;
            string symbol = GameMemoryAllocation.LargeUnitSymbol(unit);
            double increase = GameMemoryAllocation.ToLargeUnits(result.AvailableIncreaseBytes, unit);
            double before = result.Before.Available(unit);
            double after = result.After.Available(unit);
            App.Logger.Information("Memory", $"内存优化完成: 检查={result.InspectedProcesses}, 成功={result.TrimmedProcesses}, 跳过={result.SkippedProcesses}, 失败={result.FailedProcesses}, " + $"可用内存={before:0.00}→{after:0.00} {symbol}, 用时={result.Duration.TotalMilliseconds:0} ms.");
            if (showNotifications)
            {
                string message = result.AvailableIncreaseBytes > 0.01 * 1024 * 1024 * 1024
                    ? string.Format(LocalizationManager.Get("memoryOptimization.completed"), increase, after, symbol)
                    : string.Format(LocalizationManager.Get("memoryOptimization.noIncrease"), after, symbol);
                LauncherToasts.Success(message);
            }

            completion.TrySetResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            App.Logger.Information("Memory", "内存优化已取消; 已经完成的工作集整理不会撤销.");
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception error)
        {
            App.Logger.Error("Memory", "内存优化失败.", error);
            if (showNotifications) LauncherToasts.Error(LocalizationManager.Get("memoryOptimization.failed"));
            completion.TrySetException(error);
        }
        finally
        {
            _active = null;
            Changed?.Invoke();
        }
    }
}
