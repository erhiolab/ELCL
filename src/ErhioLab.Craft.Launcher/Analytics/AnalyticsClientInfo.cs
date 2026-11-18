using System.IO;
using System.Reflection;
using System.Security;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Analytics;

internal static class AnalyticsClientInfo
{
    private const string WindowsVersionKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const int FirstWindows11Build = 22000;

    internal static string LauncherVersion { get; } = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";

    internal static string WindowsVersion { get; } = ReadWindowsVersion();

    private static string ReadWindowsVersion()
    {
        try
        {
            var installationType = Registry.GetValue(WindowsVersionKey, "InstallationType", null) as string;
            return ClassifyWindowsVersion(Environment.OSVersion.Version, installationType);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return "Windows";
        }
    }

    internal static string ClassifyWindowsVersion(Version version, string? installationType)
    {
        if (installationType is "Server" or "Server Core") return "Windows Server";
        if (installationType != "Client" || version.Major != 10) return "Windows";

        return version.Build >= FirstWindows11Build ? "Windows 11" : "Windows 10";
    }
}
