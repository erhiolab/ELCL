namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>判断两个附加加载器版本能否放在同一个游戏实例中.</summary>
public static class MinecraftLoaderCompatibility
{
    /// <summary>只允许已明确验证的组合; OptiFine 可以单独使用, 或与指定 Forge 版本搭配.</summary>
    public static bool CanCombine(
        string gameVersion,
        MinecraftLoaderKind firstKind,
        MinecraftLoaderVersion first,
        MinecraftLoaderKind secondKind,
        MinecraftLoaderVersion second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (firstKind == secondKind)
        {
            return false;
        }

        if (firstKind == MinecraftLoaderKind.Forge && secondKind == MinecraftLoaderKind.OptiFine)
        {
            return IsOptiFineCompatibleWithForge(gameVersion, first.Version, second.RequiredForgeVersion);
        }

        if (firstKind == MinecraftLoaderKind.OptiFine && secondKind == MinecraftLoaderKind.Forge)
        {
            return IsOptiFineCompatibleWithForge(gameVersion, second.Version, first.RequiredForgeVersion);
        }

        return false;
    }

    private static bool IsOptiFineCompatibleWithForge(string gameVersion, string forgeVersion, string? requiredForge)
    {
        // PCL2 对 Minecraft 1.13 到 1.14.3 的 Forge + OptiFine 组合直接禁用.
        if (Version.TryParse(gameVersion, out Version? game)
            && game >= new Version(1, 13)
            && game <= new Version(1, 14, 3))
        {
            return false;
        }

        if (requiredForge is null)
        {
            return false;
        }

        if (requiredForge.Length == 0)
        {
            return true;
        }

        return requiredForge.Contains('.')
            ? string.Equals(forgeVersion, requiredForge, StringComparison.OrdinalIgnoreCase)
            : string.Equals(forgeVersion.Split('.').Last(), requiredForge, StringComparison.OrdinalIgnoreCase);
    }
}
