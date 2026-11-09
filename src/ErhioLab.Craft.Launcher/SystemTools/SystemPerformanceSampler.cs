using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal sealed class SystemPerformanceSampler : IDisposable
{
    private long _idle, _kernel, _user, _ticks;
    private double _processSeconds;
    private ProcessorUtilityCounter? _utility;
    private readonly Action<Exception>? _onUtilityUnavailable;

    internal SystemPerformanceSampler(Action<Exception>? onUtilityUnavailable = null)
    {
        _onUtilityUnavailable = onUtilityUnavailable;
        try { _utility = new ProcessorUtilityCounter(); }
        catch (Exception error) when (error is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            _onUtilityUnavailable?.Invoke(error);
        }
    }

    internal static string CpuName()
    {
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (cpu?.GetValue("ProcessorNameString") as string)?.Trim() ?? RuntimeInformation.ProcessArchitecture.ToString();
    }

    internal static GraphicsAdapterInfo[] DisplayAdapters() => GraphicsHardware.Read();

    internal (double Cpu, double ProcessCpu, long WorkingSet) Sample()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var process = Process.GetCurrentProcess();
        var ticks = Stopwatch.GetTimestamp();
        double processSeconds = process.TotalProcessorTime.TotalSeconds;
        long total = kernel - _kernel + user - _user;
        double cpu = _ticks == 0 || total <= 0 ? 0 : Math.Clamp((total - (idle - _idle)) * 100d / total, 0, 100);
        if (_utility is not null)
        {
            try { cpu = _utility.Sample(); }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            {
                _utility.Dispose();
                _utility = null;
                _onUtilityUnavailable?.Invoke(error);
            }
        }
        double processCpu = _ticks == 0
            ? 0
            : Math.Clamp((processSeconds - _processSeconds) * 100 / Stopwatch.GetElapsedTime(_ticks, ticks).TotalSeconds / Environment.ProcessorCount, 0, 100);
        (_idle, _kernel, _user, _ticks, _processSeconds) = (idle, kernel, user, ticks, processSeconds);
        return (cpu, processCpu, process.WorkingSet64);
    }

    public void Dispose()
    {
        _utility?.Dispose();
        _utility = null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
