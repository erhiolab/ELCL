using System.Buffers.Binary;
using System.IO.Compression;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>只读取存档列表所需的 NBT 字段, 不加载实体和区块数据.</summary>
internal sealed partial class MinecraftWorldMetadata(byte[] data, CancellationToken cancellationToken) : MinecraftNbtReader(data, cancellationToken)
{
    private const int MaxInflatedBytes = 32 * 1024 * 1024;
    private string? _name;
    private int? _mode;
    private bool _hardcore;
    private long? _lastPlayed;
    private string? _version;
    private long? _seed;
    private long? _legacySeed;
    private int? _difficulty;
    private bool? _allowCommands;
    private int? _dataVersion;
    private bool _hasData;

    internal static LocalMinecraftWorld Read(string metadata, LocalMinecraftWorld world, CancellationToken token)
    {
        var reader = Open(metadata, token);
        reader.Compound(0, Scope.Root);
        if (!reader._hasData) throw new InvalidDataException("世界元数据缺少 Data 标签.");
        return world with
        {
            Name = string.IsNullOrWhiteSpace(reader._name) ? world.Name : reader._name,
            GameMode = reader._hardcore ? MinecraftWorldGameMode.Hardcore
            : reader._mode is >= 0 and <= 3 ? (MinecraftWorldGameMode)reader._mode.Value : null,
            LastPlayed = reader._lastPlayed is > 0 and <= 253402300799999L
                ? DateTimeOffset.FromUnixTimeMilliseconds(reader._lastPlayed.Value)
                : null,
            GameVersion = reader._version,
            Seed = reader._seed ?? reader._legacySeed,
            Difficulty = reader._difficulty is >= 0 and <= 3 ? (MinecraftWorldDifficulty)reader._difficulty.Value : null,
            AllowCommands = reader._allowCommands,
            DataVersion = reader._dataVersion
        };
    }

    internal static long? ReadSeed(string metadata, CancellationToken token)
    {
        var reader = Open(metadata, token);
        reader.Compound(0, Scope.SeedRoot);
        return reader._seed;
    }

    private static MinecraftWorldMetadata Open(string metadata, CancellationToken token)
    {
        using var input = File.Open(metadata, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int size;
        while ((size = gzip.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            if (output.Length + size > MaxInflatedBytes) throw new InvalidDataException("解压后的世界元数据过大.");
            output.Write(buffer, 0, size);
        }

        var reader = new MinecraftWorldMetadata(output.ToArray(), token);
        if (reader.Byte() != 10) throw new InvalidDataException("世界元数据不是 NBT 复合标签.");
        reader.SkipString();
        return reader;
    }

    private enum Scope
    {
        Root,
        Data,
        Version,
        WorldGenSettings,
        SeedRoot,
        DifficultySettings
    }

    private void Compound(int depth, Scope scope)
    {
        int tag;
        while ((tag = Byte()) != 0)
        {
            Token.ThrowIfCancellationRequested();
            string name = ReadString();
            switch (scope, tag, name)
            {
                case (Scope.Root, 10, "Data"):
                    _hasData = true;
                    Compound(depth + 1, Scope.Data);
                    break;
                case (Scope.Data, 10, "Version"):
                    Compound(depth + 1, Scope.Version);
                    break;
                case (Scope.Data, 10, "WorldGenSettings"):
                case (Scope.SeedRoot, 10, "data" or "Data"):
                    Compound(depth + 1, Scope.WorldGenSettings);
                    break;
                case (Scope.Data, 10, "difficulty_settings"):
                    Compound(depth + 1, Scope.DifficultySettings);
                    break;
                case (Scope.Data, 8, "LevelName"): _name = ReadString(); break;
                case (Scope.Data, 3, "GameType"): _mode = Int(); break;
                case (Scope.Data, 1, "hardcore"): _hardcore = Byte() != 0; break;
                case (Scope.Data, 1, "Difficulty"): _difficulty = Byte(); break;
                case (Scope.DifficultySettings, 8, "difficulty"):
                    _difficulty = ReadString() switch { "peaceful" => 0, "easy" => 1, "normal" => 2, "hard" => 3, _ => null };
                    break;
                case (Scope.Data, 1, "allowCommands"): _allowCommands = Byte() != 0; break;
                case (Scope.Data, 3, "DataVersion"): _dataVersion = Int(); break;
                case (Scope.Data, 4, "RandomSeed"): _legacySeed = BinaryPrimitives.ReadInt64BigEndian(Take(8)); break;
                case (Scope.WorldGenSettings, 4, "seed"): _seed = BinaryPrimitives.ReadInt64BigEndian(Take(8)); break;
                case (Scope.SeedRoot, 4, "seed"): _seed = BinaryPrimitives.ReadInt64BigEndian(Take(8)); break;
                case (Scope.Data, 4, "LastPlayed"): _lastPlayed = BinaryPrimitives.ReadInt64BigEndian(Take(8)); break;
                case (Scope.Version, 8, "Name"): _version = ReadString(); break;
                default: Skip(tag, depth + 1); break;
            }
        }
    }

}
