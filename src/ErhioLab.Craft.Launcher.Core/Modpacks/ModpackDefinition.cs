using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Core.Modpacks;

public enum ModpackFormat
{
    Modrinth,
    CurseForge,
    MultiMc
}

public sealed record ModpackFile(string Path, IReadOnlyList<Uri> Downloads, long? Size = null, string? Sha1 = null, string? Sha512 = null, string? ProjectId = null, string? FileId = null);

public sealed record ModpackOverride(string ArchivePath, string Path, long Size);

public sealed record ModpackDefinition(string ArchivePath, ModpackFormat Format, string Name, string GameVersion, IReadOnlyList<MinecraftLoaderSelection> Loaders, IReadOnlyList<ModpackFile> Files, IReadOnlyList<ModpackOverride> Overrides);

public sealed record ModpackDownload(DownloadFileRequest Request, string? Sha512);
