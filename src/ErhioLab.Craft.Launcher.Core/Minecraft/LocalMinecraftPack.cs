namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public enum MinecraftPackKind
{
    ResourcePack,
    ShaderPack
}

/// <summary>包元数据的格式版本, 次版本号可以省略.</summary>
public readonly record struct MinecraftPackFormat(int Major, int? Minor = null);

/// <summary>带样式的纯文本片段, 颜色使用 #RRGGBB 格式.</summary>
public sealed record MinecraftTextSpan(
    string Text,
    string? Color = null,
    bool Bold = false,
    bool Italic = false,
    bool Underlined = false,
    bool Strikethrough = false);

public sealed record LocalMinecraftPack(string Path, string Name, bool IsDirectory, long? Size = null)
{
    public string? Author { get; init; }
    public string? Version { get; init; }
    public string? MinecraftVersions { get; init; }
    public MinecraftPackFormat? MinimumFormat { get; init; }
    public MinecraftPackFormat? MaximumFormat { get; init; }
    public IReadOnlyList<MinecraftTextSpan> Description { get; init; } = [];
    public byte[]? Icon { get; init; }
    public string? Error { get; init; }
}
