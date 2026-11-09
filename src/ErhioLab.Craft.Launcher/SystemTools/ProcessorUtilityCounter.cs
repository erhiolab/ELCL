using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>读取考虑处理器运行频率的效用百分比, 与任务管理器使用相同指标.</summary>
internal sealed class ProcessorUtilityCounter : IDisposable
{
    private readonly QueryHandle _query;
    private readonly nint _counter;
    private long _lastCollection;
    private double _lastValue;

    internal ProcessorUtilityCounter()
    {
        Check(PdhOpenQuery(null, 0, out _query));
        try
        {
            // English 接口由 PDH 处理本地化, 不依赖 Windows 的显示语言.
            Check(PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", 0, out _counter));
            Check(PdhCollectQueryData(_query));
            _lastCollection = Stopwatch.GetTimestamp();
        }
        catch
        {
            _query.Dispose();
            throw;
        }
    }

    internal double Sample()
    {
        long now = Stopwatch.GetTimestamp();
        // 初始化/主题切换可能连续请求, 避免用几毫秒的差值计算利用率.
        if (Stopwatch.GetElapsedTime(_lastCollection, now) < TimeSpan.FromMilliseconds(100)) return _lastValue;
        Check(PdhCollectQueryData(_query));
        Check(PdhGetFormattedCounterValue(_counter, 0x200 | 0x8000, out _, out var value));
        if (value.Status is not (0 or 1)) Check(value.Status);
        if (!double.IsFinite(value.Value)) throw new InvalidOperationException("CPU 效用计数器返回了无效值.");
        _lastCollection = now;
        _lastValue = Math.Clamp(value.Value, 0, 100);
        return _lastValue;
    }

    public void Dispose() => _query.Dispose();

    private static void Check(uint status)
    {
        if (status != 0) throw new Win32Exception(unchecked((int)status), $"读取 CPU 性能计数器失败 (0x{status:X8}).");
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    private sealed class QueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public QueryHandle() : base(true)
        {
        }

        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, nuint userData, out QueryHandle query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(QueryHandle query, string path, nuint userData, out nint counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(QueryHandle query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(nint query);
}
