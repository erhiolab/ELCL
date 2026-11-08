using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>读取调用方指定的游戏目录, 不决定目录位置或界面行为.</summary>
public static class LocalMinecraftVersionScanner
{
    /// <summary>查找 versions 下每个版本目录的 JSON, 忽略单个损坏的版本文件.</summary>
    public static IReadOnlyList<LocalMinecraftVersion> Scan(string gameDirectory, Action<string, Exception>? onFileError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        string versionsDirectory = Path.Combine(Path.GetFullPath(gameDirectory), "versions");
        if (!Directory.Exists(versionsDirectory))
        {
            return [];
        }

        List<LocalMinecraftVersion> versions = [];
        foreach (string folder in Directory.EnumerateDirectories(versionsDirectory))
        {
            string folderName = Path.GetFileName(folder);
            string jsonPath = Path.Combine(folder, folderName + ".json");
            try
            {
                if (!File.Exists(jsonPath))
                {
                    jsonPath = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;
                    if (jsonPath.Length == 0)
                    {
                        continue;
                    }
                }

                using FileStream stream = File.OpenRead(jsonPath);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("id", out JsonElement idElement) ||
                    idElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(idElement.GetString()))
                {
                    continue;
                }

                string id = idElement.GetString()!;
                string type = root.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;
                DateTimeOffset? releaseTime = null;
                if (root.TryGetProperty("releaseTime", out JsonElement timeElement) && timeElement.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(timeElement.GetString(), out DateTimeOffset parsedTime))
                {
                    releaseTime = parsedTime;
                }

                (string? loader, string? loaderVersion) = ReadLoader(root, id);
                string? gameVersion = ReadGameVersion(root, id, versionsDirectory);
                versions.Add(new LocalMinecraftVersion(id, folder, jsonPath, type, releaseTime, gameVersion, loader, loaderVersion));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                onFileError?.Invoke(jsonPath, exception);
            }
        }

        return versions.OrderByDescending(version => version.ReleaseTime)
            .ThenBy(version => version.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadGameVersion(JsonElement root, string id, string versionsDirectory)
    {
        string? clientVersion = ReadString(root, "clientVersion");
        if (!string.IsNullOrWhiteSpace(clientVersion)) return clientVersion;
        string? fmlVersion = ReadGameArgument(root, "--fml.mcVersion");
        if (!string.IsNullOrWhiteSpace(fmlVersion)) return fmlVersion;
        string? inherited = ReadString(root, "inheritsFrom");
        if (inherited is not null)
        {
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { id };
            string current = inherited;
            while (visited.Add(current))
            {
                if (LooksLikeGameVersion(current)) return current;
                string parentPath = Path.Combine(versionsDirectory, current, current + ".json");
                if (!File.Exists(parentPath)) break;
                try
                {
                    using JsonDocument parent = JsonDocument.Parse(File.ReadAllText(parentPath));
                    string? next = ReadString(parent.RootElement, "inheritsFrom");
                    if (next is null) break;
                    current = next;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    break;
                }
            }
        }

        if (LooksLikeGameVersion(id)) return id;
        return MatchLoaderName(id)?.Groups["game"].Value;
    }

    private static bool LooksLikeGameVersion(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+\.\d+(?:\.\d+)?(?:-(?:pre|rc)\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{2}w\d{2}[a-z]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(value, @"^(?:rd-\d{6}(?:\d{2})?|(?:in|inf)-\d{8})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(value, @"^[abc]\d+(?:\.\d+)+[a-z]?(?:_\d+[a-z]?)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static (string? Loader, string? Version) ReadLoader(JsonElement root, string id)
    {
        string? neoForge = ReadGameArgument(root, "--fml.neoForgeVersion");
        if (neoForge is not null) return ("NeoForge", neoForge);
        string? forge = ReadGameArgument(root, "--fml.forgeVersion");
        if (forge is not null) return ("Forge", forge);
        if (root.TryGetProperty("libraries", out JsonElement libraries) && libraries.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement library in libraries.EnumerateArray())
            {
                string? name = ReadString(library, "name");
                if (name is null) continue;
                string[] parts = name.Split(':');
                if (parts.Length < 3) continue;
                string? loader = (parts[0], parts[1]) switch
                {
                    ("net.fabricmc", "fabric-loader") => "Fabric",
                    ("org.quiltmc", "quilt-loader") => "Quilt",
                    ("net.neoforged", "neoforge" or "forge") => "NeoForge",
                    ("net.minecraftforge", "forge" or "fmlloader") => "Forge",
                    ("com.mumfrey", "liteloader") => "LiteLoader",
                    _ => null
                };
                if (loader is not null)
                {
                    string version = parts[2];
                    string? gameVersion = ReadString(root, "clientVersion") ?? ReadString(root, "inheritsFrom");
                    if (loader == "Forge" && gameVersion is not null && version.StartsWith(gameVersion + "-", StringComparison.OrdinalIgnoreCase))
                        version = version[(gameVersion.Length + 1)..];
                    return (loader, version);
                }
            }
        }

        System.Text.RegularExpressions.Match? match = MatchLoaderName(id);
        if (match is not null) return (match.Groups["loader"].Value, match.Groups["version"].Value);
        return (null, null);
    }

    private static System.Text.RegularExpressions.Match? MatchLoaderName(string id)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(id,
            @"^(?<game>\d+\.\d+(?:\.\d+)?)(?:[-_ ])(?<loader>NeoForge|Fabric|Quilt|Forge|LiteLoader|OptiFine)[-_ ](?:loader[-_])?(?<version>\d[\w.+-]*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match : null;
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadGameArgument(JsonElement root, string flag)
    {
        if (!root.TryGetProperty("arguments", out JsonElement arguments) || arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("game", out JsonElement game) || game.ValueKind != JsonValueKind.Array)
            return null;
        bool nextIsValue = false;
        foreach (JsonElement item in game.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            string? value = item.GetString();
            if (nextIsValue) return value;
            nextIsValue = string.Equals(value, flag, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }
}
