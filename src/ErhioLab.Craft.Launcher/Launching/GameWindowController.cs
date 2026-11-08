using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Microsoft.Win32;

using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Launching;

internal static class GameWindowController
{
    internal static void SetGpuPreference(string executable, bool highPerformance)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
        string previous = key.GetValue(executable) as string ?? "";
        string preference = Regex.Replace(previous, @"(?:^|;)GpuPreference=\d+;?", ";", RegexOptions.CultureInvariant).Trim(';');
        if (highPerformance) preference = (preference.Length > 0 ? preference + ";" : "") + "GpuPreference=2;";
        if (preference.Length == 0) key.DeleteValue(executable, false);
        else key.SetValue(executable, preference, RegistryValueKind.String);
    }

    internal static nint FindWindow(Process process)
    {
        process.Refresh();
        if (process.MainWindowHandle is var main && main != 0 && IsWindowVisible(main)) return main;
        nint found = 0;
        EnumWindows((window, _) =>
        {
            if (GetWindowThreadProcessId(window, out uint id) == 0) return true;
            if (id != process.Id || !IsWindowVisible(window) || GetWindow(window, 4) != 0) return true;
            found = window;
            return false;
        }, 0);
        return found;
    }

    internal static void ApplyWindow(nint window, string titleTemplate, MinecraftLaunchPlaceholderContext context, bool maximize)
    {
        if (!string.IsNullOrWhiteSpace(titleTemplate))
            SetWindowText(window, MinecraftLaunchPlaceholders.Expand(titleTemplate, context with { Timestamp = DateTimeOffset.Now }));
        if (maximize) ShowWindowAsync(window, 3);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);

    private delegate bool EnumWindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowText(nint window, string text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);
}
