using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal sealed record ModpackAssociationStatus(bool RegisteredHere, bool DefaultHere, bool RegisteredElsewhere);

/// <summary>仅在用户操作时注册当前用户的 MRPACK 打开方式, 不修改 Windows UserChoice.</summary>
internal static class ModpackFileAssociation
{
    internal const string ProgId = "ErhioLab.ELCL.Modpack";
    private const string Extension = @"Software\Classes\.mrpack";
    private const string Program = @"Software\Classes\" + ProgId;
    private const string Application = @"Software\Classes\Applications\ErhioLab.Craft.Launcher.exe";
    private const string Capabilities = @"Software\ErhioLab\ELCL\FileAssociation\Capabilities";

    internal static string CurrentCommand => BuildCommand(
        Environment.ProcessPath ?? throw new InvalidOperationException("无法确定启动器程序路径."),
        Path.Combine(AppContext.BaseDirectory, typeof(App).Assembly.GetName().Name + ".dll"));
    internal static string CurrentIcon => "\"" + Path.Combine(AppContext.BaseDirectory, "ErhioLab.Craft.Launcher.exe") + "\",0";

    internal static string BuildCommand(string executable, string assemblyPath)
    {
        static string Quote(string value)
        {
            if (!Path.IsPathFullyQualified(value) || value.Any(character => character < 32 || character == '"')) throw new ArgumentException("程序路径无效.");
            return "\"" + value + "\"";
        }

        return Quote(executable) + (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? " " + Quote(assemblyPath) : "")
                                 + " --import-modpack \"%1\"";
    }

    internal static ModpackAssociationStatus Status() => ReadStatus(Registry.CurrentUser, CurrentCommand);

    internal static ModpackAssociationStatus ReadStatus(RegistryKey user, string command)
    {
        string? registered = Value(user, Program + @"\shell\open\command", "");
        bool here = string.Equals(command, registered, StringComparison.OrdinalIgnoreCase);
        string? choice = Value(user, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mrpack\UserChoice", "ProgId");
        string? current = choice ?? Value(user, Extension, "");
        return new(here, here && string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase), registered is not null && !here);
    }

    internal static ModpackAssociationStatus Register(string friendlyName)
    {
        RegisterAt(Registry.CurrentUser, CurrentCommand, CurrentIcon, friendlyName);
        NotifyShell();
        return Status();
    }

    internal static void RegisterAt(RegistryKey user, string command, string icon, string friendlyName)
    {
        using var extension = user.CreateSubKey(Extension);
        using var program = user.CreateSubKey(Program);
        // 首次覆盖普通扩展名关联时保留原值; 再次绑定或移动安装位置不丢弃备份.
        string? previous = extension.GetValue("") as string;
        if (previous != ProgId)
        {
            program.SetValue("HadPreviousAssociation", previous is null ? 0 : 1, RegistryValueKind.DWord);
            program.SetValue("PreviousAssociation", previous ?? "");
        }

        program.SetValue("", friendlyName);
        Set(user, Program + @"\DefaultIcon", "", icon);
        Set(user, Program + @"\shell\open\command", "", command);
        extension.SetValue("", ProgId);
        Set(user, Extension + @"\OpenWithProgids", ProgId, "");
        Set(user, Application, "FriendlyAppName", "ELCL");
        Set(user, Application + @"\shell\open\command", "", command);
        Set(user, Application + @"\SupportedTypes", ".mrpack", "");
        Set(user, Capabilities, "ApplicationName", "ELCL");
        Set(user, Capabilities, "ApplicationDescription", friendlyName);
        Set(user, Capabilities + @"\FileAssociations", ".mrpack", ProgId);
        Set(user, @"Software\RegisteredApplications", "ELCL", Capabilities);
    }

    /// <returns>Windows 保护的默认项仍指向 ELCL 时返回 true, 需要在系统设置中另选应用.</returns>
    internal static bool Unregister()
    {
        bool protectedDefault = UnregisterAt(Registry.CurrentUser, CurrentCommand);
        NotifyShell();
        return protectedDefault;
    }

    internal static bool UnregisterAt(RegistryKey user, string command)
    {
        var status = ReadStatus(user, command);
        if (!status.RegisteredHere) return false;
        string? previous = Value(user, Program, "PreviousAssociation");
        using (var program = user.OpenSubKey(Program))
        using (var extension = user.OpenSubKey(Extension, true))
        {
            if (extension?.GetValue("") as string == ProgId)
            {
                if (program?.GetValue("HadPreviousAssociation") is int existed && existed == 1 && previous is not null && previous != ProgId)
                    extension.SetValue("", previous);
                else extension.DeleteValue("", false);
            }
        }

        using (var openWith = user.OpenSubKey(Extension + @"\OpenWithProgids", true)) openWith?.DeleteValue(ProgId, false);
        if (string.Equals(Value(user, Application + @"\shell\open\command", ""), command, StringComparison.OrdinalIgnoreCase)) user.DeleteSubKeyTree(Application, false);
        if (Value(user, @"Software\RegisteredApplications", "ELCL") == Capabilities)
        {
            using var applications = user.OpenSubKey(@"Software\RegisteredApplications", true);
            applications?.DeleteValue("ELCL", false);
        }

        user.DeleteSubKeyTree(Program, false);
        user.DeleteSubKeyTree(@"Software\ErhioLab\ELCL\FileAssociation", false);
        return Value(user, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mrpack\UserChoice", "ProgId") == ProgId;
    }

    internal static void OpenDefaultApps() => Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });

    private static string? Value(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }

    private static void Set(RegistryKey root, string path, string name, string value)
    {
        using var key = root.CreateSubKey(path);
        key.SetValue(name, value);
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
