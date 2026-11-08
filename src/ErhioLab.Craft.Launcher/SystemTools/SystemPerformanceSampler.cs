using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal sealed class SystemPerformanceSampler
{
    private long _idle, _kernel, _user, _ticks;
    private double _processSeconds;

    internal static string CpuName()
    {
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (cpu?.GetValue("ProcessorNameString") as string)?.Trim() ?? RuntimeInformation.ProcessArchitecture.ToString();
    }

    internal static string[] DisplayAdapters()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var video = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
        foreach (var id in video?.GetSubKeyNames() ?? [])
        {
            using var adapter = video!.OpenSubKey(id + @"\0000");
            if (adapter?.GetValue("DriverDesc") is string name && !string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
        }

        return names.ToArray();
    }

    internal (double Cpu, double ProcessCpu, long WorkingSet) Sample()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var process = Process.GetCurrentProcess();
        var ticks = Stopwatch.GetTimestamp();
        double processSeconds = process.TotalProcessorTime.TotalSeconds;
        long total = kernel - _kernel + user - _user;
        double cpu = _ticks == 0 || total <= 0 ? 0 : Math.Clamp((total - (idle - _idle)) * 100d / total, 0, 100);
        double processCpu = _ticks == 0
            ? 0
            : Math.Clamp((processSeconds - _processSeconds) * 100 / Stopwatch.GetElapsedTime(_ticks, ticks).TotalSeconds / Environment.ProcessorCount, 0, 100);
        (_idle, _kernel, _user, _ticks, _processSeconds) = (idle, kernel, user, ticks, processSeconds);
        return (cpu, processCpu, process.WorkingSet64);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
