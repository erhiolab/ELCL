using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ErhioLab.Craft.Launcher.Settings;

internal readonly record struct PhysicalMemorySnapshot(ulong TotalBytes, ulong AvailableBytes)
{
    private const double BytesPerGiB = 1024d * 1024 * 1024;
    internal double TotalGiB => TotalBytes / BytesPerGiB;
    internal double AvailableGiB => Math.Min(AvailableBytes, TotalBytes) / BytesPerGiB;
    internal double UsedGiB => TotalGiB - AvailableGiB;
}

/// <summary>读取当前物理内存并估算设置页中的游戏内存分配, 不预占或清理系统内存.</summary>
internal static class GameMemoryAllocation
{
    internal static PhysicalMemorySnapshot ReadPhysicalMemory()
    {
        MemoryStatus status = new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (status.TotalPhysical == 0) throw new InvalidOperationException("系统返回的物理内存总量为零.");
        return new PhysicalMemorySnapshot(status.TotalPhysical, status.AvailablePhysical);
    }

    internal static double CalculateGiB(double availableGiB, bool automatic, int customPercent)
    {
        if (!double.IsFinite(availableGiB) || availableGiB <= 0) return 0;
        if (!automatic) return availableGiB * Math.Clamp(customPercent, 0, 100) / 100;

        // 分段预算: 1.5 / 2.5 / 4 / 8 GiB.
        // 越过各档位后, 新增可用内存中分别只取 70%/40%/15%.
        double remaining = availableGiB;
        double allocation = 0;
        (double Budget, double Ratio)[] stages = [(1.5, 1), (1, 0.7), (1.5, 0.4), (4, 0.15)];
        foreach ((double budget, double ratio) in stages)
        {
            double consumed = Math.Min(remaining, budget / ratio);
            allocation += consumed * ratio;
            remaining -= consumed;
            if (remaining <= 0) break;
        }

        // 内存极少时也不让预览超过实际可用量.
        return Math.Min(availableGiB, Math.Round(Math.Max(allocation, 0.5), 1));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
