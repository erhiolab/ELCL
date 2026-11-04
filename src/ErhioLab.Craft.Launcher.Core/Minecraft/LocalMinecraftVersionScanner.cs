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

                versions.Add(new LocalMinecraftVersion(id, folder, jsonPath, type, releaseTime));
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
}
