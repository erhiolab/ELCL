using System.Diagnostics.CodeAnalysis;

namespace ErhioLab.Craft.Launcher.Core.Modpacks;

public enum ModpackExportCategory
{
    GameSettings,
    PersonalData,
    Mods,
    Configuration,
    ResourcePacks,
    ShaderPacks,
    Saves,
    Screenshots,
    Logs,
    Cache,
    Other,
    Runtime
}

public sealed record ModpackExportFile(string Path, long Size, DateTime LastWriteTimeUtc, ModpackExportCategory Category, bool DefaultSelected, bool CanSelect = true);

public sealed record ModpackExportScan(string VersionDirectory, string GameVersion, IReadOnlyDictionary<string, string> Dependencies, IReadOnlyList<ModpackExportFile> Files);

public sealed record ModpackExportOptions(string Name, string Version, IReadOnlyList<string> SelectedPaths, bool BundleResources = false, bool OnlyModrinth = false, bool OverwriteExisting = false);

public sealed record ModpackExportProgress(double Percent, string Path);

[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global", Justification = "Export statistics are part of the public library API.")]
public sealed record ModpackExportResult(int EmbeddedFiles, int ReferencedFiles, int LookupFailures, long SelectedBytes);
