using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Versions;

/// <summary>版本列表和概览共用的图标/说明与标签.</summary>
public record LocalVersionPresentation(LocalMinecraftVersion Version, string? CustomDescription = null)
{
    public Geometry Icon => !Version.IsValid
        ? LauncherIcons.Error
        : Version.Loader is { } loader && Enum.TryParse(loader, true, out MinecraftLoaderKind kind)
            ? LauncherIcons.ForLoader(kind)
            : Version.Type switch
            {
                "snapshot" => LauncherIcons.VersionPreview,
                "old_alpha" or "old_beta" => LauncherIcons.VersionAncient,
                _ => LauncherIcons.VersionRelease
            };

    public bool HasParsedVersion => Version.IsValid && !string.IsNullOrWhiteSpace(Version.GameVersion)
                                                    && (Version.Loader is null || !string.IsNullOrWhiteSpace(Version.LoaderVersion));

    public bool HasLoader => HasParsedVersion && Version.Loader is not null;
    public bool HasTypeBadge => Version.IsValid && !string.IsNullOrWhiteSpace(CustomDescription);
    public string GameBadge => Version.GameVersion ?? "";
    public string LoaderBadge => Version.Loader is null ? "" : $"{Version.Loader} {Version.LoaderVersion}";

    public string TypeText => LocalizationManager.Get(Version.Type switch
    {
        "release" => "version.release",
        "snapshot" => "versionSelector.snapshotType",
        "old_alpha" or "old_beta" => "version.oldVersions",
        _ => "versionSelector.customVersion"
    });

    public string StatusText => !Version.IsValid ? LocalizationManager.Get(Version.Error switch
        {
            LocalMinecraftVersionError.MissingMetadata => "versionSelector.missingMetadata",
            LocalMinecraftVersionError.InvalidMetadata => "versionSelector.invalidMetadata",
            _ => "versionSelector.unreadableMetadata"
        }) :
        HasTypeBadge ? CustomDescription! :
        HasParsedVersion ? TypeText : LocalizationManager.Get("versionSelector.parseFailed");
}
