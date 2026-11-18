using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Core.Modpacks;

/// <summary>扫描版本内容并导出通用整合包; 路径/文件选择和网络客户端由调用方提供.</summary>
public static partial class ModpackExporter
{
    private const int MaximumFiles = 99000;
    private const long MaximumFile = 8L * 1024 * 1024 * 1024;
    private const long MaximumTotal = 64L * 1024 * 1024 * 1024;

    public static async Task<ModpackExportScan> ScanAsync(string gameDirectory, LocalMinecraftVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        string directory = Path.GetFullPath(version.FolderPath);
        RequireNoLinks(directory, directory);
        var metadata = await MinecraftLaunchVersion.LoadAsync(gameDirectory, version.JsonPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        string game = version.GameVersion ?? metadata.GameVersion;
        Dictionary<string, string> dependencies = new(StringComparer.Ordinal) { ["minecraft"] = game };
        using (var json = JsonDocument.Parse(metadata.Json))
        {
            if (json.RootElement.TryGetProperty("libraries", out var libraries))
                foreach (var library in libraries.EnumerateArray())
                {
                    if (!library.TryGetProperty("name", out var name)) continue;
                    string[] parts = (name.GetString() ?? "").Split(':');
                    if (parts.Length < 3) continue;
                    string? loader = (parts[0], parts[1]) switch
                    {
                        ("net.fabricmc", "fabric-loader") => "fabric-loader",
                        ("org.quiltmc", "quilt-loader") => "quilt-loader",
                        ("net.neoforged", "neoforge" or "forge") => "neoforge",
                        ("net.minecraftforge", "forge" or "fmlloader") => "forge",
                        ("com.mumfrey", "liteloader") => "liteloader",
                        ("optifine", "OptiFine") => "optifine",
                        _ => null
                    };
                    if (loader is not null)
                        dependencies[loader] = loader == "forge" && parts[2].StartsWith(game + "-", StringComparison.Ordinal)
                            ? parts[2][(game.Length + 1)..]
                            : parts[2];
                }
        }

        string? fallback = version.Loader?.ToLowerInvariant() switch { "fabric" => "fabric-loader", "quilt" => "quilt-loader", var value => value };
        if (fallback is not null && version.LoaderVersion is { Length: > 0 } loaderVersion) dependencies.TryAdd(fallback, loaderVersion);
        var files = await Task.Run(() => ScanFiles(directory, version.JsonPath, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new(directory, game, dependencies, files);
    }

    private static IReadOnlyList<ModpackExportFile> ScanFiles(string root, string metadataPath, CancellationToken token)
    {
        List<ModpackExportFile> files = [];
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    files.Add(new(relative, 0, DateTime.MinValue, ModpackExportCategory.Runtime, false, false));
                else if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else
                {
                    var info = new FileInfo(entry);
                    ModpackExportCategory category = Category(relative);
                    bool selectable = category != ModpackExportCategory.Runtime && !string.Equals(Path.GetFullPath(entry), Path.GetFullPath(metadataPath), StringComparison.OrdinalIgnoreCase)
                                                                                && !relative.Equals(Path.GetFileName(root) + ".jar", StringComparison.OrdinalIgnoreCase);
                    if (!selectable) category = ModpackExportCategory.Runtime;
                    bool selected = selectable && category is ModpackExportCategory.GameSettings or ModpackExportCategory.Mods or ModpackExportCategory.Configuration
                        or ModpackExportCategory.ResourcePacks or ModpackExportCategory.ShaderPacks;
                    if (relative.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".disable", StringComparison.OrdinalIgnoreCase)) selected = false;
                    files.Add(new(relative, info.Length, info.LastWriteTimeUtc, category, selected, selectable));
                }

                if (files.Count > MaximumFiles) throw new InvalidDataException("版本文件过多, 无法导出超过 99000 个条目.");
            }
        }

        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ModpackExportCategory Category(string path)
    {
        string top = path.Split('/')[0].ToLowerInvariant();
        return top switch
        {
            "elcl" or "pcl" or "pcl2" or "hmcl" or "libraries" or "assets" or "natives" or ".fabric" or ".quilt" or "launcher_profiles.json" or "launcher_accounts.json" => ModpackExportCategory.Runtime,
            "mods" => ModpackExportCategory.Mods,
            "config" or "defaultconfigs" or "kubejs" or "scripts" => ModpackExportCategory.Configuration,
            "resourcepacks" => ModpackExportCategory.ResourcePacks,
            "shaderpacks" => ModpackExportCategory.ShaderPacks,
            "saves" => ModpackExportCategory.Saves,
            "screenshots" => ModpackExportCategory.Screenshots,
            "logs" or "crash-reports" => ModpackExportCategory.Logs,
            "cache" or "caches" or ".cache" => ModpackExportCategory.Cache,
            "servers.dat" or "servers.dat_old" or "hotbar.nbt" or "command_history.txt" or "quickplay" => ModpackExportCategory.PersonalData,
            _ when top.StartsWith("options", StringComparison.Ordinal) && top.EndsWith(".txt", StringComparison.Ordinal) => ModpackExportCategory.GameSettings,
            _ => ModpackExportCategory.Other
        };
    }

    internal static string SourcePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Split('/').Any(part => part is "" or "." or ".." || part.Any(c => c < 32 || "<>:\"|?*".Contains(c))
                                                                                                                                        || part.EndsWith('.') || part.EndsWith(' ') || System.Text.RegularExpressions.Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("导出路径必须是安全的相对路径: " + relative);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("导出路径超出版本目录.");
        RequireNoLinks(root, path);
        return path;
    }

    private static void RequireNoLinks(string root, string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("导出不允许链接文件或目录: " + current);
            if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) break;
        }
    }
}
