using System.Text.Json.Nodes;

using ErhioLab.Craft.Launcher.Core.Downloads;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public sealed record MinecraftLoaderSelection(MinecraftLoaderKind Kind, MinecraftLoaderVersion Version);

/// <summary>由安装器准备的依赖与处理步骤. 工作目录和 Java 进程的执行由调用方管理.</summary>
public sealed class MinecraftLoaderInstallPlan
{
    public required string GameDirectory { get; init; }
    public required string GameVersion { get; init; }
    public required string InstalledName { get; init; }
    public required string WorkDirectory { get; init; }
    public string? BaseVersionJsonPath { get; init; }
    public string? BaseClientJarPath { get; init; }
    public IReadOnlyList<DownloadFileRequest> Files => Dependencies;
    public IReadOnlyList<string> VersionFiles => WrittenVersionFiles;

    public bool NeedsJava => Processes.Count > 0 || ForgeProfiles.Any(profile => profile.Metadata["processors"] is JsonArray processors
                                                                                 && processors.OfType<JsonObject>().Any(processor => processor["sides"] is not JsonArray sides || sides.Any(side => side?.GetValue<string>() == "client")));

    internal JsonObject VersionJson { get; set; } = new();
    internal List<DownloadFileRequest> Dependencies { get; } = [];
    internal List<(JsonObject Metadata, string Installer, string Extracted)> ForgeProfiles { get; } = [];
    internal List<(string ClassPath, string MainClass, string[] Args)> Processes { get; } = [];
    internal List<(string Path, string? Sha1)> GeneratedFiles { get; } = [];
    internal List<(string Source, string Target)> Copies { get; } = [];
    internal List<string> WrittenVersionFiles { get; } = [];
    internal MinecraftManifestSource Source { get; init; }
    internal Func<IReadOnlyList<DownloadFileRequest>, CancellationToken, Task> Download { get; init; } = null!;
    internal Action<string>? Log { get; init; }
}
