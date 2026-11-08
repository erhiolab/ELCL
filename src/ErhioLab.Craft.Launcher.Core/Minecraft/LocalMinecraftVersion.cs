namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>本地版本元数据的读取错误, 不表示游戏资源已完成完整性校验.</summary>
public enum LocalMinecraftVersionError
{
    None,
    MissingMetadata,
    InvalidMetadata,
    UnreadableMetadata
}

/// <summary>本地版本目录及其元数据; 错误条目的 Id 使用目录名称.</summary>
public sealed record LocalMinecraftVersion(
    string Id,
    string FolderPath,
    string JsonPath,
    string Type,
    DateTimeOffset? ReleaseTime,
    string? GameVersion = null,
    string? Loader = null,
    string? LoaderVersion = null
)
{
    public LocalMinecraftVersionError Error { get; init; }
    public bool IsValid => Error == LocalMinecraftVersionError.None;
}
