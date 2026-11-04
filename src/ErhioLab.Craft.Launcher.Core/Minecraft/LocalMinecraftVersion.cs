namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>从游戏目录中的版本 JSON 读取到的本地版本.</summary>
public sealed record LocalMinecraftVersion(
    string Id,
    string FolderPath,
    string JsonPath,
    string Type,
    DateTimeOffset? ReleaseTime
);
