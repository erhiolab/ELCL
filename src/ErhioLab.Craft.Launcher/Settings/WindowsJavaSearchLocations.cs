using System.IO;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>将 Windows 注册表中的 Java 安装位置提供给通用 Core 查找器.</summary>
internal static class WindowsJavaSearchLocations
{
    internal static IReadOnlyList<string> GetRoots()
    {
        string[] keys =
        [
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\Eclipse Adoptium\JDK",
            @"SOFTWARE\Microsoft\JDK"
        ];
        HashSet<string> locations = new(StringComparer.OrdinalIgnoreCase);
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (string keyPath in keys)
                    {
                        using RegistryKey? key = baseKey.OpenSubKey(keyPath);
                        if (key is null) continue;
                        AddHome(key, locations);
                        foreach (string name in key.GetSubKeyNames())
                        {
                            using RegistryKey? version = key.OpenSubKey(name);
                            if (version is not null) AddHome(version, locations);
                        }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // 某个注册表视图不可读时, 其他来源仍可使用.
                }
            }
        }

        return locations.ToArray();
    }

    private static void AddHome(RegistryKey key, HashSet<string> locations)
    {
        foreach (string value in new[] { "JavaHome", "Path", "InstallationPath" })
        {
            if (key.GetValue(value) is string home && !string.IsNullOrWhiteSpace(home))
            {
                locations.Add(home);
            }
        }
    }
}
