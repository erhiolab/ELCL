using System.IO;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal static class WallpaperEngineCatalog
{
    private static readonly Uri ApiEndpoint = new("http://127.0.0.1:26384/api/wallpaper/list");

    private static readonly HttpClient ApiClient = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromMilliseconds(1500),
        MaxResponseContentBufferSize = 4 * 1024 * 1024
    };

    private static readonly Regex LibraryPathLine = new("^\\s*\"(?:path|[0-9]+)\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly EnumerationOptions ProjectEnumeration = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    internal static Task<(LauncherBackgroundFile[] Files, bool UsedApi)> LoadAsync() => LoadAsync(DiscoverLibraries(), ApiClient);

    internal static async Task<(LauncherBackgroundFile[] Files, bool UsedApi)> LoadAsync(IEnumerable<string> libraries, HttpClient client)
    {
        var roots = libraries.SelectMany(library => new[]
        {
            new ProjectRoot(Path.Combine(library, "steamapps", "workshop", "content", "431960"), true),
            new ProjectRoot(Path.Combine(library, "steamapps", "common", "wallpaper_engine", "projects", "myprojects"), false)
        }).Where(root => IsPlainDirectory(root.Path)).ToArray();
        if (roots.Length == 0) return ([], false);

        // 非正式的本地接口只提供项目线索, 不允许它越过已发现的 Steam 项目目录读取文件.
        var apiProjects = await ReadApiProjectsAsync(roots, client).ConfigureAwait(false);
        var files = ReadProjects(apiProjects);
        if (files.Length != 0) return (files, true);

        return (ReadProjects(roots.SelectMany(ScanRoot)), false);
    }

    internal static string[] DiscoverLibraries()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in new[]
                 {
                     (@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"),
                     (@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"),
                     (@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                 })
        {
            try
            {
                AddLibrary(steamRoots, Registry.GetValue(key, name, null) as string);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }

        AddLibrary(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots)
        {
            var config = new FileInfo(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"));
            try
            {
                if (!config.Exists || config.Length > 4 * 1024 * 1024) continue;
                foreach (var library in ReadLibraryPaths(File.ReadLines(config.FullName))) AddLibrary(libraries, library);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
            {
            }
        }

        return libraries.ToArray();
    }

    internal static IEnumerable<string> ReadLibraryPaths(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var match = LibraryPathLine.Match(line);
            if (match.Success) yield return match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal);
        }
    }

    private static void AddLibrary(HashSet<string> libraries, string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || path.Length < 3 || path[1] != ':') return;
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (Directory.Exists(fullPath)) libraries.Add(fullPath);
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        {
        }
    }

    private static async Task<List<Project>> ReadApiProjectsAsync(ProjectRoot[] roots, HttpClient client)
    {
        var projects = new List<Project>();
        try
        {
            using var response = await client.GetAsync(ApiEndpoint).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return projects;
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var items = json.RootElement;
            if (items.ValueKind == JsonValueKind.Object && items.TryGetProperty("wallpapers", out var array)) items = array;
            if (items.ValueKind != JsonValueKind.Array) return projects;
            foreach (var item in items.EnumerateArray())
            {
                var path = GetText(item, "file") ?? GetText(item, "preview");
                if (!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && FindProject(roots, path) is { } project) projects.Add(project);
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or JsonException or ArgumentException or NotSupportedException)
        {
        }

        return projects;
    }

    private static Project? FindProject(ProjectRoot[] roots, string path)
    {
        var fullPath = Path.GetFullPath(path);
        foreach (var root in roots)
        {
            var relative = LauncherBackgroundFiles.NormalizeRelativePath(Path.GetRelativePath(root.Path, fullPath));
            if (relative is null) continue;
            var separator = relative.IndexOf(Path.DirectorySeparatorChar);
            if (separator > 0) return new Project(Path.Combine(root.Path, relative[..separator]), root.IsWorkshop);
        }

        return null;
    }

    private static Project[] ScanRoot(ProjectRoot root)
    {
        try
        {
            return Directory.EnumerateDirectories(root.Path, "*", ProjectEnumeration).Select(path => new Project(path, root.IsWorkshop)).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static LauncherBackgroundFile[] ReadProjects(IEnumerable<Project> projects)
    {
        var files = new Dictionary<string, LauncherBackgroundFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects.Distinct())
            if (ReadProject(project) is { WallpaperId: { } id } file)
                files.TryAdd(id, file);

        return files.Values.OrderBy(file => file.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(file => file.WallpaperId, StringComparer.Ordinal).ToArray();
    }

    private static LauncherBackgroundFile? ReadProject(Project project)
    {
        try
        {
            if (!IsPlainDirectory(project.Path)) return null;
            var manifest = new FileInfo(Path.Combine(project.Path, "project.json"));
            if (!manifest.Exists || manifest.Length > 1024 * 1024 || manifest.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            using var reader = new StreamReader(manifest.FullName);
            using var json = JsonDocument.Parse(reader.ReadToEnd());
            var root = json.RootElement;
            var type = GetText(root, "type")?.Trim().ToLowerInvariant();
            if (type is not (null or "" or "video" or "image")) return null;
            var path = GetMediaPath(project.Path, GetText(root, "file"));
            if (path is null) return null;
            var preview = GetMediaPath(project.Path, GetText(root, "preview"));
            if (preview is not null && LauncherBackgroundFiles.IsVideo(Path.GetExtension(preview))) preview = null;

            var folderName = Path.GetFileName(project.Path);
            var key = project.IsWorkshop && folderName.All(char.IsAsciiDigit)
                ? "workshop:" + folderName
                : "project:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.Path.ToUpperInvariant())));
            var title = GetText(root, "title");
            return new LauncherBackgroundFile(path, Path.GetRelativePath(project.Path, path), LauncherBackgroundFiles.IsVideo(Path.GetExtension(path)))
            {
                Title = string.IsNullOrWhiteSpace(title) ? folderName : title,
                WallpaperId = key,
                PreviewPath = preview
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? GetMediaPath(string project, string? entry)
    {
        if (LauncherBackgroundFiles.NormalizeRelativePath(entry) is not { } relative || !LauncherBackgroundFiles.IsSupported(Path.GetExtension(relative))) return null;
        var segments = relative.Split(Path.DirectorySeparatorChar);
        var path = project;
        foreach (var segment in segments.SkipLast(1))
        {
            path = Path.Combine(path, segment);
            if (!IsPlainDirectory(path)) return null;
        }

        var file = new FileInfo(Path.Combine(path, segments[^1]));
        return file.Exists && !file.Attributes.HasFlag(FileAttributes.ReparsePoint) ? file.FullName : null;
    }

    private static bool IsPlainDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        return directory.Exists && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static string? GetText(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record ProjectRoot(string Path, bool IsWorkshop);

    private sealed record Project(string Path, bool IsWorkshop);
}
