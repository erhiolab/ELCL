using System.Runtime.InteropServices;

namespace ErhioLab.Craft.Launcher.Core.Java;

public enum JavaDistribution
{
    Mojang,
    Adoptium,
    Azul,
    Semeru
}

public enum JavaPackageType
{
    Jre,
    Jdk
}

public enum JavaDownloadFormat
{
    Zip,
    TarGZip,
    Manifest
}

public enum JavaRuntimeEntryType
{
    File,
    Directory,
    Link
}

/// <summary>下载目标, 操作系统使用 windows/linux/macos; 默认检测操作系统架构, 不使用进程架构.</summary>
public sealed record JavaPlatform(string OperatingSystem, Architecture Architecture)
{
    public static JavaPlatform Current => new(
        System.OperatingSystem.IsWindows() ? "windows"
        : System.OperatingSystem.IsLinux() ? "linux"
        : System.OperatingSystem.IsMacOS() ? "macos" : throw new PlatformNotSupportedException(), RuntimeInformation.OSArchitecture);
}

/// <summary>可直接部署的 Java 发行包; 清单类型的大小和 SHA-1 指向清单本身.</summary>
public sealed record JavaDownloadVersion(
    JavaDistribution Distribution,
    int MajorVersion,
    string Version,
    JavaPackageType PackageType,
    string Name,
    JavaPlatform Platform,
    JavaDownloadFormat Format,
    Uri DownloadUri,
    long? Size = null,
    DateTimeOffset? ReleasedAt = null,
    string? Sha256 = null,
    string? Sha1 = null,
    Uri? MetadataUri = null,
    Uri? ChecksumUri = null);

public sealed record JavaVersionPage(IReadOnlyList<JavaDownloadVersion> Versions, bool HasMore);

/// <summary>Mojang 清单条目, 路径相对于运行时根目录; 链接目标仍需由安装方验证.</summary>
public sealed record JavaRuntimeEntry(
    string Path,
    JavaRuntimeEntryType Type,
    Uri? DownloadUri = null,
    long? Size = null,
    string? Sha1 = null,
    bool Executable = false,
    string? LinkTarget = null);

/// <summary>已补齐校验信息的安装数据; 压缩包的 Files 为空, 清单类型返回文件/目录/链接条目.</summary>
public sealed record JavaDownloadPackage(JavaDownloadVersion Version, IReadOnlyList<JavaRuntimeEntry> Files);
