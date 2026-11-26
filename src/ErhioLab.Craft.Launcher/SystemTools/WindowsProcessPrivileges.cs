using System.Security.Principal;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal static class WindowsProcessPrivileges
{
    private static readonly Lazy<bool> Elevated = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    });

    /// <summary>当前启动器进程是否具有有效的管理员权限.</summary>
    internal static bool IsElevated => Elevated.Value;
}
