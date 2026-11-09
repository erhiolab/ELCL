using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>Windows 报告的物理核心/逻辑线程和核心能效分组.</summary>
internal sealed record ProcessorTopology(int Cores, int Threads, int? PerformanceCores, int? EfficiencyCores)
{
    internal static ProcessorTopology Read()
    {
        uint length = 0;
        if (!GetLogicalProcessorInformationEx(0, 0, ref length) && Marshal.GetLastWin32Error() != 122)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (length is < 8 or > 16 * 1024 * 1024) throw new InvalidOperationException("处理器拓扑数据大小无效.");
            nint buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                uint capacity = length;
                if (!GetLogicalProcessorInformationEx(0, buffer, ref length))
                {
                    if (Marshal.GetLastWin32Error() == 122) continue;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (length > capacity) throw new InvalidOperationException("处理器拓扑数据超出缓冲区.");
                return Parse(buffer, checked((int)length));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("处理器拓扑变化过快, 无法读取.");
    }

    private static ProcessorTopology Parse(nint buffer, int length)
    {
        int cores = 0, threads = 0;
        var efficiencies = new Dictionary<byte, int>();
        for (int offset = 0; offset < length;)
        {
            if (length - offset < 8) throw new InvalidOperationException("处理器拓扑记录不完整.");
            nint record = buffer + offset;
            int size = Marshal.ReadInt32(record, 4);
            if (size < 8 || size > length - offset) throw new InvalidOperationException("处理器拓扑记录大小无效.");
            if (Marshal.ReadInt32(record) == 0)
            {
                if (size < 32) throw new InvalidOperationException("处理器核心记录不完整.");
                int groups = (ushort)Marshal.ReadInt16(record, 30);
                int affinitySize = IntPtr.Size + 8;
                if (groups == 0 || groups > (size - 32) / affinitySize) throw new InvalidOperationException("处理器组记录无效.");
                cores++;
                byte efficiency = Marshal.ReadByte(record, 9);
                efficiencies[efficiency] = efficiencies.GetValueOrDefault(efficiency) + 1;
                for (int group = 0; group < groups; group++)
                {
                    int maskOffset = 32 + group * affinitySize;
                    ulong mask = IntPtr.Size == 8
                        ? unchecked((ulong)Marshal.ReadInt64(record, maskOffset))
                        : unchecked((uint)Marshal.ReadInt32(record, maskOffset));
                    threads += BitOperations.PopCount(mask);
                }
            }

            offset += size;
        }

        if (cores == 0 || threads == 0) throw new InvalidOperationException("未读取到处理器核心.");
        // 较高 EfficiencyClass 表示较高性能.
        return efficiencies.Count == 2
            ? new(cores, threads, efficiencies[efficiencies.Keys.Max()], efficiencies[efficiencies.Keys.Min()])
            : new(cores, threads, null, null);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, nint buffer, ref uint length);
}
