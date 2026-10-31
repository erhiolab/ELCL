using System.Reflection;

namespace ErhioLab.Craft.Launcher.Core;

/// <summary>
/// 提供启动器核心的名称和版本信息.
/// </summary>
public static class CoreInfo
{
    /// <summary>核心组件的完整名称.</summary>
    public const string ProductName = "ErhioLab Craft Launcher Core";

    /// <summary>核心组件的简称.</summary>
    public const string ShortName = "ELCLC";

    /// <summary>当前加载的核心程序集版本, 例如 0.0.1.</summary>
    public static string Version { get; } = typeof(CoreInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(CoreInfo).Assembly.GetName().Version?.ToString() ?? "unknown";
}
