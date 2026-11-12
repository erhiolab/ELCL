using System.IO;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Java;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>客户端 Java 列表项, 将安装来源与通用探测结果分开保存; 兼容旧配置的四个基本字段.</summary>
public sealed record LauncherJavaInstallation(
    string Path,
    string Version,
    int MajorVersion,
    string Architecture,
    bool InstalledByLauncher = false,
    string? InstallationDirectory = null,
    Guid? InstallationId = null)
{
    internal JavaInstallation ToJava() => new(Path, Version, MajorVersion, Architecture);
    internal LauncherJavaInstallation WithProbe(JavaInstallation java) => this with { Path = java.Path, Version = java.Version, MajorVersion = java.MajorVersion, Architecture = java.Architecture };

    internal static LauncherJavaInstallation FromJava(JavaInstallation java, LauncherJavaInstallation? previous = null)
    {
        if (previous is not null) return previous.WithProbe(java);
        var entry = new LauncherJavaInstallation(java.Path, java.Version, java.MajorVersion, java.Architecture);
        string? home = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(java.Path));
        if (home is null) return entry;
        try
        {
            var marker = JsonSerializer.Deserialize<ManagedJavaMarker>(File.ReadAllText(System.IO.Path.Combine(home, ManagedJavaMarker.FileName)));
            if (marker is { InstallationId: var id } && id != Guid.Empty && marker.Executable == "bin/java.exe"
                && string.Equals(System.IO.Path.GetFullPath(java.Path), System.IO.Path.GetFullPath(System.IO.Path.Combine(home, "bin", "java.exe")), StringComparison.OrdinalIgnoreCase))
                return entry with { InstalledByLauncher = true, InstallationDirectory = home, InstallationId = id };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return entry;
    }
}

internal sealed record ManagedJavaMarker(Guid InstallationId, string Executable = "bin/java.exe")
{
    internal const string FileName = ".elcl-java.json";
}
