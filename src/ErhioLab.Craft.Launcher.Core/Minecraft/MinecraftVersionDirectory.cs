using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>管理调用方指定的版本目录, 不选择游戏路径/弹窗/配置或任务策略.</summary>
public static class MinecraftVersionDirectory
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static IReadOnlyList<string> GetReferences(string gameDirectory, string versionDirectory)
    {
        string folder = ValidateDirectory(gameDirectory, versionDirectory);
        string name = Path.GetFileName(folder);
        var references = new List<string>();
        foreach (var version in LocalMinecraftVersionScanner.Scan(gameDirectory))
        {
            if (string.Equals(version.FolderPath, folder, PathComparison)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(version.JsonPath));
            foreach (string property in new[] { "inheritsFrom", "jar" })
                if (document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), name, PathComparison))
                {
                    references.Add(Path.GetFileName(version.FolderPath));
                    break;
                }
        }

        return references;
    }

    /// <summary>重命名目录及同名 JSON/JAR, 保留实际游戏版本; 失败时回滚已更改的名称和 JSON.</summary>
    public static async Task<LocalMinecraftVersion> RenameAsync(string gameDirectory, string versionDirectory, string newName,
        CancellationToken cancellationToken = default)
    {
        string originalFolder = ValidateDirectory(gameDirectory, versionDirectory);
        MinecraftVanillaDownloadPlanner.ValidateVersionName(newName);
        string oldName = Path.GetFileName(originalFolder);
        var version = LocalMinecraftVersionScanner.Scan(gameDirectory, includeInvalidVersions: true)
            .Single(item => string.Equals(item.FolderPath, originalFolder, PathComparison));
        if (oldName == newName) return version;
        string destination = Path.Combine(Path.GetDirectoryName(originalFolder)!, newName);
        if (!string.Equals(originalFolder, destination, PathComparison) && (Directory.Exists(destination) || File.Exists(destination)))
            throw new IOException("A version with this name already exists.");
        RejectReferences(gameDirectory, originalFolder);
        string originalJsonName = Path.GetFileName(version.JsonPath), newJsonName = newName + ".json";
        string originalJarName = oldName + ".jar", newJarName = newName + ".jar";
        foreach (var (oldFile, newFile) in new[] { (originalJsonName, newJsonName), (originalJarName, newJarName) })
            if (!string.Equals(oldFile, newFile, PathComparison) && File.Exists(Path.Combine(originalFolder, newFile)))
                throw new IOException("The renamed metadata or JAR would overwrite an existing file.");

        byte[]? originalBytes = File.Exists(version.JsonPath) ? await File.ReadAllBytesAsync(version.JsonPath, cancellationToken).ConfigureAwait(false) : null;
        byte[]? updatedBytes = originalBytes;
        if (version.IsValid && originalBytes is not null)
        {
            var json = JsonNode.Parse(originalBytes) as JsonObject ?? throw new InvalidDataException("Version metadata must be an object.");
            json["id"] = newName;
            if (json["clientVersion"] is null && version.GameVersion is { } gameVersion) json["clientVersion"] = gameVersion;
            if (json["jar"]?.GetValue<string>() is { } jar && string.Equals(jar, oldName, PathComparison)) json["jar"] = newName;
            updatedBytes = Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string temporaryFolder = Path.Combine(Path.GetDirectoryName(originalFolder)!, ".elcl-rename-" + Guid.NewGuid().ToString("N"));
        string currentFolder = originalFolder;
        string backupName = ".elcl-metadata-" + Guid.NewGuid().ToString("N") + ".bak";
        bool backedUp = false, wroteJson = false, movedJar = false;
        try
        {
            // 提交阶段不在文件操作之间响应取消, 以免留下半套名称.
            if (updatedBytes is not null)
            {
                File.Move(Path.Combine(currentFolder, originalJsonName), Path.Combine(currentFolder, backupName));
                backedUp = true;
                wroteJson = true;
                File.WriteAllBytes(Path.Combine(currentFolder, newJsonName), updatedBytes);
            }

            if (File.Exists(Path.Combine(currentFolder, originalJarName)) && originalJarName != newJarName)
            {
                File.Move(Path.Combine(currentFolder, originalJarName), Path.Combine(currentFolder, newJarName));
                movedJar = true;
            }

            // 中间目录使 Windows 上仅更改大小写的重命名同样可用.
            Directory.Move(currentFolder, temporaryFolder);
            currentFolder = temporaryFolder;
            Directory.Move(currentFolder, destination);
            currentFolder = destination;
            if (backedUp) File.Delete(Path.Combine(currentFolder, backupName));
        }
        catch (Exception failure)
        {
            try
            {
                if (currentFolder != originalFolder) Directory.Move(currentFolder, originalFolder);
                if (movedJar) File.Move(Path.Combine(originalFolder, newJarName), Path.Combine(originalFolder, originalJarName));
                if (wroteJson) File.Delete(Path.Combine(originalFolder, newJsonName));
                if (backedUp) File.Move(Path.Combine(originalFolder, backupName), Path.Combine(originalFolder, originalJsonName), true);
            }
            catch (Exception rollback)
            {
                throw new AggregateException("Version rename and rollback failed; preserve the directory for recovery.", failure, rollback);
            }

            throw;
        }

        return version with { Id = newName, FolderPath = destination, JsonPath = Path.Combine(destination, newJsonName) };
    }

    /// <summary>删除单个版本目录; 拒绝越界/链接目录和被其他版本引用的目录.</summary>
    public static void Delete(string gameDirectory, string versionDirectory)
    {
        string folder = ValidateDirectory(gameDirectory, versionDirectory);
        RejectReferences(gameDirectory, folder);
        Directory.Delete(folder, true);
    }

    private static void RejectReferences(string gameDirectory, string folder)
    {
        var references = GetReferences(gameDirectory, folder);
        if (references.Count > 0) throw new InvalidOperationException("Version is referenced by: " + string.Join(", ", references));
    }

    private static string ValidateDirectory(string gameDirectory, string versionDirectory)
    {
        string root = Path.GetFullPath(Path.Combine(gameDirectory, "versions"));
        string folder = Path.GetFullPath(versionDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(folder), root, PathComparison) || !Directory.Exists(folder)
                                                                                || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint) || File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Version directory is missing, outside the versions folder or a symbolic link.");
        return folder;
    }
}
