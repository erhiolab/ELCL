namespace ErhioLab.Craft.Launcher.Core.Java;

/// <summary>经过执行验证的本机 Java 运行时.</summary>
public sealed record JavaInstallation(string Path, string Version, int MajorVersion, string Architecture);
