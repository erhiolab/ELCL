using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ErhioLab.Craft.Launcher.Settings;

internal readonly record struct PhysicalMemorySnapshot(ulong TotalBytes, ulong AvailableBytes)
{
    internal ulong ClampedAvailableBytes => Math.Min(AvailableBytes, TotalBytes);
    internal ulong UsedBytes => TotalBytes - ClampedAvailableBytes;
}

/// <summary>读取当前物理内存并估算设置页中的游戏内存分配, 不预占或清理系统内存.</summary>
internal static class GameMemoryAllocation
{
    private const double BytesPerGiB = 1024d * 1024 * 1024;
    private const double BytesPerGb = 1000d * 1000 * 1000;
    internal const ulong BytesPerMiB = 1024UL * 1024;

    internal static PhysicalMemorySnapshot ReadPhysicalMemory()
    {
        MemoryStatus status = new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (status.TotalPhysical == 0) throw new InvalidOperationException("系统返回的物理内存总量为零.");
        return new PhysicalMemorySnapshot(status.TotalPhysical, status.AvailablePhysical);
    }

    internal static ulong CalculateBytes(ulong availableBytes, bool automatic, int customPercent, MemoryUnitSystem unit)
    {
        if (availableBytes == 0) return 0;
        if (!automatic)
        {
            // 直接按字节计算比例, 避免切换显示单位带来的浮点取整差异和乘法溢出.
            var percent = (ulong)Math.Clamp(customPercent, 0, 100);
            return availableBytes / 100 * percent + availableBytes % 100 * percent / 100;
        }

        var bytesPerUnit = BytesPerAllocationUnit(unit);
        var allocationBytes = CalculateAutomatic(availableBytes / bytesPerUnit) * bytesPerUnit;
        // 在转换回整数之前封顶, 保证预览和启动值都不超过实际可用内存.
        return allocationBytes >= availableBytes ? availableBytes : (ulong)Math.Floor(allocationBytes);
    }

    // 自动分配的预算始终按 GB/GiB 计算, 不随显示单位升档改变.
    private static double BytesPerAllocationUnit(MemoryUnitSystem unit) => unit == MemoryUnitSystem.Decimal ? BytesPerGb : BytesPerGiB;

    private static double CalculateAutomatic(double available)
    {
        if (!double.IsFinite(available) || available <= 0) return 0;

        // 分段预算: 1.5 / 2.5 / 4 / 8 个当前大单位(GiB 或 GB).
        // 越过各档位后, 新增可用内存中分别只取 70%/40%/15%.
        double remaining = available;
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
        return Math.Min(available, Math.Round(Math.Max(allocation, 0.5), 1));
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
