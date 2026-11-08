using System.Text.Json.Nodes;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>已合并继承链的启动元数据; 路径和父版本获取方式均由调用方提供.</summary>
public sealed record MinecraftLaunchVersion(
    string Json,
    string GameDirectory,
    string VersionDirectory,
    string ClientJarPath,
    string GameVersion,
    int JavaMajorVersion,
    bool HasCustomClientJar = false)
{
    public static async Task<MinecraftLaunchVersion> LoadAsync(string gameDirectory, string versionJsonPath,
        Func<string, CancellationToken, Task<string>>? fetchParent = null, CancellationToken cancellationToken = default)
    {
        string game = Path.GetFullPath(gameDirectory);
        string selected = Path.GetFullPath(versionJsonPath);
        string folder = Path.GetDirectoryName(selected)!;
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);

        async Task<(JsonObject Root, string Jar)> Read(string path, string? supplied, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 32 || !visited.Add(path)) throw new InvalidDataException("版本继承存在循环或超过 32 层.");
            JsonObject child = JsonNode.Parse(supplied ?? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) as JsonObject
                               ?? throw new InvalidDataException("版本 JSON 必须是对象.");
            string currentFolder = Path.GetDirectoryName(path)!;
            string jar = Path.Combine(currentFolder, Path.GetFileName(currentFolder) + ".jar");
            if (child["inheritsFrom"]?.GetValue<string>() is { Length: > 0 } parent)
            {
                string parentPath = SafePath(game, "versions/" + SafeName(parent) + "/" + parent + ".json");
                string? json = File.Exists(parentPath)
                    ? null
                    : fetchParent is not null
                        ? await fetchParent(parent, cancellationToken).ConfigureAwait(false)
                        : throw new FileNotFoundException("缺少父版本 JSON: " + parent, parentPath);
                var inherited = await Read(parentPath, json, depth + 1).ConfigureAwait(false);
                if (child["downloads"]?["client"] is null) jar = inherited.Jar;
                if (child["clientVersion"] is null) child["clientVersion"] = inherited.Root["clientVersion"]?.DeepClone() ?? inherited.Root["id"]?.DeepClone();
                child = Merge(inherited.Root, child);
            }

            if (child["jar"]?.GetValue<string>() is { Length: > 0 } jarName)
                jar = SafePath(game, "versions/" + SafeName(jarName) + "/" + jarName + ".jar");
            return (child, jar);
        }

        var result = await Read(selected, null, 0).ConfigureAwait(false);
        string gameVersion = result.Root["clientVersion"]?.GetValue<string>() ?? result.Root["id"]?.GetValue<string>()
            ?? throw new InvalidDataException("版本 JSON 缺少 id.");
        int java = result.Root["javaVersion"]?["majorVersion"]?.GetValue<int>() ?? GuessJava(gameVersion);
        if (java < 8 || java > 100) throw new InvalidDataException("版本要求的 Java 版本无效.");
        if (string.IsNullOrWhiteSpace(result.Root["mainClass"]?.GetValue<string>()))
            throw new InvalidDataException("版本 JSON 缺少 mainClass, 无法启动.");
        return new(result.Root.ToJsonString(), game, folder, result.Jar, gameVersion, java, result.Root["jar"] is not null);
    }

    /// <summary>保守匹配旧版 Java; Java 21 及以上接受不低于元数据要求的运行时.</summary>
    public bool IsJavaCompatible(int major) => major == JavaMajorVersion || JavaMajorVersion == 16 && major == 17 || JavaMajorVersion >= 21 && major >= JavaMajorVersion;

    private static int GuessJava(string version)
    {
        string[] parts = version.Split('.');
        if (int.TryParse(parts[0], out int major) && major >= 26) return 25;
        if (parts.Length > 1 && parts[0] == "1" && int.TryParse(parts[1], out int minor))
        {
            if (minor >= 21 || minor == 20 && parts.Length > 2 && int.TryParse(parts[2], out int patch) && patch >= 5) return 21;
            return minor >= 18 ? 17 : minor == 17 ? 16 : 8;
        }

        return 8;
    }

    private static JsonObject Merge(JsonObject parent, JsonObject child)
    {
        JsonObject result = (JsonObject)parent.DeepClone();
        foreach (var (key, value) in child)
        {
            if (key == "libraries" && value is JsonArray additions)
            {
                List<JsonNode?> libraries = (result[key] as JsonArray ?? []).Select(node => node?.DeepClone()).ToList();
                foreach (JsonNode? entry in additions)
                {
                    string? coordinate = entry?["name"]?.GetValue<string>();

                    string LibraryKey(string name)
                    {
                        string[] split = name.Split(':');
                        return split.Length < 3 ? name : split[0] + ":" + split[1] + (split.Length > 3 ? ":" + split[3] : "");
                    }

                    if (coordinate is not null) libraries.RemoveAll(node => node?["name"]?.GetValue<string>() is { } name && LibraryKey(name) == LibraryKey(coordinate));
                    libraries.Add(entry?.DeepClone());
                }

                result[key] = new JsonArray(libraries.ToArray());
            }
            else if (key == "arguments" && value is JsonObject arguments)
            {
                JsonObject combined = result[key] as JsonObject ?? new();
                foreach (var (kind, values) in arguments)
                    combined[kind] = new JsonArray((combined[kind] as JsonArray ?? []).Concat(values as JsonArray ?? []).Select(node => node?.DeepClone()).ToArray());
                if (result[key] is null) result[key] = combined;
            }
            else if (value is JsonObject nested && result[key] is JsonObject baseObject) result[key] = Merge(baseObject, nested);
            else result[key] = value?.DeepClone();
        }

        result.Remove("inheritsFrom");
        return result;
    }

    internal static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Contains('/') || value.Contains('\\') || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("版本或文件名称无效.");
        return value;
    }

    internal static string SafePath(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("文件路径超出指定目录.");
        return path;
    }
}
