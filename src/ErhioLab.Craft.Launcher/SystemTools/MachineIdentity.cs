using System.Security.Cryptography;
using System.Text;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>仅供本地使用的 Windows 安装标识, 不参与匿名统计.</summary>
internal static class MachineIdentity
{
    /// <summary>返回应用域隔离的机器码. 同一 Windows 安装内稳定, 重装或克隆系统可能改变其唯一性.</summary>
    internal static string GetMachineCode()
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        if (key?.GetValue("MachineGuid") is not string value || !Guid.TryParse(value, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("无法读取 Windows 安装标识.");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ELCL.machine-code.v1:" + id.ToString("D"))))
            .ToLowerInvariant();
    }
}
