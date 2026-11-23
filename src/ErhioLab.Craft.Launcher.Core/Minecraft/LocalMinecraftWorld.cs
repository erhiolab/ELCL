namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public enum MinecraftWorldGameMode
{
    Survival,
    Creative,
    Adventure,
    Spectator,
    Hardcore
}

public enum MinecraftWorldDifficulty
{
    Peaceful,
    Easy,
    Normal,
    Hard
}

public sealed record LocalMinecraftWorld(string DirectoryPath, string Name)
{
    public MinecraftWorldGameMode? GameMode { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }
    public long? Size { get; init; }
    public string? GameVersion { get; init; }
    public long? Seed { get; init; }
    public MinecraftWorldDifficulty? Difficulty { get; init; }
    public bool? AllowCommands { get; init; }
    public int? DataVersion { get; init; }
    public byte[]? Icon { get; init; }
    public string? MetadataError { get; init; }
    public bool UsedPreviousMetadata { get; init; }
}

public sealed record MinecraftWorldBackup(string FileName, DateTimeOffset CreatedAt, long Size);
