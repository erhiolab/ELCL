using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Views.Download;

namespace ErhioLab.Craft.Launcher.Mods;

internal static class CommunityResourceCatalog
{
    internal static string TitleKey(ModProjectKind kind) => kind switch
    {
        ModProjectKind.Modpack => "community.modpacks",
        ModProjectKind.DataPack => "community.dataPacks",
        ModProjectKind.ResourcePack => "community.resourcePacks",
        ModProjectKind.ShaderPack => "community.shaderPacks",
        _ => "mods.title"
    };

    internal static string BackToListKey(ModProjectKind kind) => kind == ModProjectKind.Mod ? "mods.back" : TitleKey(kind) + ".back";

    internal static string L(string key) => LocalizationManager.Get("community." + key);

    internal static System.Windows.Media.Geometry Icon(ModProjectKind kind) => kind switch
    {
        ModProjectKind.Modpack => LauncherIcons.Toolbox,
        ModProjectKind.DataPack => LauncherIcons.Clover,
        ModProjectKind.ResourcePack => LauncherIcons.Image,
        ModProjectKind.ShaderPack => LauncherIcons.DayTheme,
        _ => LauncherIcons.ModsAndResources
    };

    internal static bool HasLoaderFilter(ModProjectKind kind) => kind is ModProjectKind.Mod or ModProjectKind.Modpack;

    internal static FilterOption[] LoaderOptions(ModProjectKind kind)
    {
        FilterOption[] common =
        [
            new("", CommunityMods.L("all")), new("forge", "Forge"), new("neoforge", "NeoForge"),
            new("fabric", "Fabric"), new("quilt", "Quilt")
        ];
        return kind == ModProjectKind.Mod ? [.. common, new("liteloader", "LiteLoader")] : common;
    }

    internal static CategoryOption[] Categories(ModProjectKind kind) =>
    [
        new(null, CommunityMods.L("all")), .. kind switch
        {
            ModProjectKind.Modpack => new[]
            {
                Category("technology", "technology", 4472), Category("magic", "magic", 4473),
                Category("adventure", "adventure", 4475), Category("quests", "quests", 4478), Category("lightweight", "lightweight", 4481),
                Category("combat", "combat", 4483), Category("multiplayer", "multiplayer", 4484)
            },
            ModProjectKind.DataPack => new[]
            {
                Category("adventure", "adventure", 6948), Category("technology", "technology", 6951),
                Category("magic", "magic", 6952), Category("utility", "utility", 6953), Category("library", "library", 6950)
            },
            ModProjectKind.ResourcePack => new[]
            {
                Category("16x", "16x", 393), Category("32x", "32x", 394), Category("64x", "64x", 395),
                Category("128x", "128x", 396), Category("256x", "256x", 397), Category("realistic", "realistic", 400), Category("fonts", "fonts", 5244)
            },
            ModProjectKind.ShaderPack => new[]
            {
                Category("realistic", "realistic", 6553), Category("fantasy", "fantasy", 6554),
                Category("vanillaLike", "vanilla-like", 6555)
            },
            _ => []
        }
    ];

    private static CategoryOption Category(string key, string modrinth, int curseForge) => new(new ModCategory(key, modrinth, curseForge), ModPresentation.CategoryName(key));
}
