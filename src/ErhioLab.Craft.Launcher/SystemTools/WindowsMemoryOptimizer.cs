using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal sealed record MemoryOptimizationResult(
    PhysicalMemorySnapshot Before,
    PhysicalMemorySnapshot After,
    int InspectedProcesses,
    int TrimmedProcesses,
    int SkippedProcesses,
    int FailedProcesses,
    TimeSpan Duration)
{
    internal double AvailableIncreaseGiB => After.AvailableGiB - Before.AvailableGiB;
}

/// <summary>使用 Windows 工作集接口整理允许访问的同会话进程, 不结束进程或改变其虚拟内存分配.</summary>
internal static class WindowsMemoryOptimizer
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint SetQuota = 0x0100;

    internal static MemoryOptimizationResult Optimize(IReadOnlyCollection<int> excludedProcessIds,
        CancellationToken cancellationToken, IReadOnlyCollection<int>? candidateProcessIds = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PhysicalMemorySnapshot before = GameMemoryAllocation.ReadPhysicalMemory();
        var stopwatch = Stopwatch.StartNew();
        HashSet<int> excluded = [.. excludedProcessIds, Environment.ProcessId, 0, 4];
        GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundId);
        excluded.Add((int)foregroundId);
        using var current = Process.GetCurrentProcess();
        int sessionId = current.SessionId;
        int inspected = 0, trimmed = 0, skipped = 0, failed = 0;
        int lastError = 0;
        int[] candidates;
        if (candidateProcessIds is not null) candidates = candidateProcessIds.ToArray();
        else
        {
            var processes = Process.GetProcesses();
            try
            {
                candidates = processes.Select(process => process.Id).ToArray();
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        foreach (int id in candidates.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            inspected++;
            if (excluded.Contains(id) || !ProcessIdToSessionId((uint)id, out uint candidateSession) || candidateSession != sessionId)
            {
                skipped++;
                continue;
            }

            using SafeProcessHandle process = OpenProcess(QueryLimitedInformation | SetQuota, false, id);
            if (process.IsInvalid)
            {
                // 系统/受保护进程与已经退出的进程属于正常跳过, 不提升权限或反复重试.
                skipped++;
                continue;
            }

            if (K32EmptyWorkingSet(process)) trimmed++;
            else
            {
                failed++;
                lastError = Marshal.GetLastWin32Error();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var after = GameMemoryAllocation.ReadPhysicalMemory();
        stopwatch.Stop();
        if (trimmed == 0 && failed > 0) throw new Win32Exception(lastError, "Windows 未能完成工作集整理.");
        return new(before, after, inspected, trimmed, skipped, failed, stopwatch.Elapsed);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EmptyWorkingSet(SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
