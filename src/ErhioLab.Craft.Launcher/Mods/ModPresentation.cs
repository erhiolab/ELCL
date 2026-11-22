using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Mods;

internal static class ModPresentation
{
    internal static string LoaderName(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => "Forge", "neoforge" => "NeoForge", "fabric" => "Fabric", "quilt" => "Quilt", "liteloader" => "LiteLoader", _ => loader
    };

    internal static int LoaderOrder(string loader) => loader.ToLowerInvariant() switch
    {
        "neoforge" => 0, "forge" => 1, "fabric" => 2, "quilt" => 3, "liteloader" => 4, _ => 5
    };

    internal static string CategoryName(string category)
    {
        string key = category switch
        {
            "adventure-rpg" => "adventure", "world-gen" => "worldgen", "cosmetic" => "decoration", "performance" => "optimization",
            "library-api" => "library", "map-information" => "map", "server-utility" => "management", "armor-weapons-tools" => "equipment",
            "food" => "food", "storage" => "storage", "technology-player-transport" => "transportation", "energy-fluid-and-item-transport" => "transport",
            "technology-energy" => "energy", "technology-processing" => "processing", "technology-farming" => "farming",
            "technology-genetics" => "genetics", "world-biomes" => "biomes", "world-structures" => "structures",
            "world-dimensions" => "dimensions", "world-mobs" => "mobs", "world-ores-resources" => "ores",
            "utility-qol" => "utility", "game-mechanics" => "gameMechanics", "bug-fixes" => "bugfixes",
            "adventure-and-rpg" => "adventure", "tech" => "technology", "small-light" => "lightweight", "combat-pvp" => "combat",
            "photo-realistic" => "realistic", "font-packs" => "fonts", "vanilla" or "vanilla-like" => "vanillaLike",
            "sixteen-x" => "16x", "thirty-two-x" => "32x", "sixty-four-x" => "64x", "one-twenty-eight-x" => "128x",
            "two-fifty-six-x" => "256x", _ => category
        };
        string text = CommunityMods.L("category." + key);
        return text.StartsWith("mods.category.", StringComparison.Ordinal) ? category : text;
    }

    internal static IComparer<string> VersionOrder { get; } = Comparer<string>.Create((left, right) =>
    {
        string[] first = Regex.Split(left, "([0-9]+)"), second = Regex.Split(right, "([0-9]+)");
        for (int i = 0; i < Math.Min(first.Length, second.Length); i++)
        {
            int result = long.TryParse(first[i], out long a) && long.TryParse(second[i], out long b)
                ? a.CompareTo(b)
                : string.Compare(first[i], second[i], StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
        }

        return first.Length.CompareTo(second.Length);
    });
}
