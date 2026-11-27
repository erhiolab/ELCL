using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Tasks;

namespace ErhioLab.Craft.Launcher.Mods;

internal sealed record ModBrowserContext(string? GameVersion, string? Loader, string? VersionDirectory)
{
    internal string? WorldDataPacksDirectory { get; init; }

    internal static ModBrowserContext Current()
    {
        string? path = App.Settings.SelectedVersionPath;
        if (path is null || !Directory.Exists(path)) return new(null, null, null);
        try
        {
            string? versions = Path.GetDirectoryName(path), game = versions is null ? null : Path.GetDirectoryName(versions);
            var version = game is null ? null : LocalMinecraftVersionScanner.Scan(game).FirstOrDefault(item => string.Equals(item.FolderPath, path, StringComparison.OrdinalIgnoreCase));
            return new(version?.GameVersion, CommunityMods.Loader(version?.Loader), path);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            App.Logger.Error("Mods", "读取当前游戏版本失败.", error);
            return new(null, null, path);
        }
    }
}

internal static class CommunityMods
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly HttpClient IconHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim Enrichment = new(3);
    private static readonly ConcurrentDictionary<string, Task<McModTranslation?>> Translations = new();
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Images = new();
    private static readonly ConcurrentDictionary<string, Task<ModProject?>> LocalProjects = new();
    private static readonly SemaphoreSlim LocalLookup = new(2);
    internal static event Action<string>? Installed;
    internal static string L(string key) => LocalizationManager.Get("mods." + key);
    internal static bool IsTranslationEnabled => ModLocalization.IsEnabled(App.Settings);
    internal static CommunitySource Source => App.Settings.CommunityDownloadSource == VersionListSource.MirrorFirst ? CommunitySource.MirrorFirst : CommunitySource.OfficialFirst;
    internal static ModRepositoryClient Client() => new(Http, ServiceConfiguration.CurseForgeApiKey, Source);
    internal static string? Loader(string? value) => value?.Split([',', '+', ' '], StringSplitOptions.RemoveEmptyEntries).Select(part => part.ToLowerInvariant()).FirstOrDefault(part => part is "forge" or "neoforge" or "fabric" or "quilt" or "liteloader");
    internal static bool HasLoader(LocalMinecraftVersion version) => Loader(version.Loader) is not null;
    internal static void NotifyInstalled(string directory) => Installed?.Invoke(directory);

    internal static Task<ModProject?> IdentifyAsync(LocalModInfo mod)
    {
        if (mod.Sha1.Length != 40) return Task.FromResult<ModProject?>(null);
        if (LocalProjects.TryGetValue(mod.Sha1, out var previous) && previous.IsCompletedSuccessfully && previous.Result is null) LocalProjects.TryRemove(mod.Sha1, out _);
        return LocalProjects.GetOrAdd(mod.Sha1, _ => IdentifyCoreAsync(mod));
    }

    private static async Task<ModProject?> IdentifyCoreAsync(LocalModInfo mod)
    {
        await LocalLookup.WaitAsync();
        try
        {
            string path = LauncherCache.Shared.Community("projects", mod.Sha1.ToLowerInvariant(), ".json");
            if (File.Exists(path) && !CacheFileTree.IsLinked(path))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ModProject>(await File.ReadAllBytesAsync(path)) is { } cached)
                    {
                        LauncherCache.Touch(path);
                        return cached;
                    }
                }
                catch (Exception error) when (error is JsonException or FileNotFoundException or DirectoryNotFoundException)
                {
                    // 读取期间被回收或内容失效时重新获取.
                }
            }

            var project = await Client().IdentifyAsync(mod);
            if (project is not null) await RememberProjectAsync(mod.Sha1, project);
            return project;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            App.Logger.Error("Mods", $"本地 Mods 来源信息暂不可用: {mod.FileName}.", error);
            return null;
        }
        finally
        {
            LocalLookup.Release();
        }
    }

    internal static async Task RememberProjectAsync(string sha1, ModProject project)
    {
        LocalProjects[sha1] = Task.FromResult<ModProject?>(project);
        try
        {
            string path = LauncherCache.Shared.Community("projects", sha1.ToLowerInvariant(), ".json");
            await LauncherCache.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(project));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Mods", "保存本地 Mods 来源信息失败.", error);
        }
    }

    internal static string? DefaultModDirectory(ModBrowserContext context)
    {
        string? version = context.VersionDirectory ?? App.Settings.SelectedVersionPath;
        if (version is null) return null;
        if (!Directory.Exists(version)) throw new DirectoryNotFoundException(L("directoryMissing"));
        string mods = Path.Combine(version, "mods");
        Directory.CreateDirectory(mods);
        return mods;
    }

    internal static Task<McModTranslation?> TranslationAsync(string name, ModProject? project = null)
    {
        if (!IsTranslationEnabled || project is { Kind: not ModProjectKind.Mod }) return Task.FromResult<McModTranslation?>(null);
        string key = project is null ? "query:" + name : project.Platform + ":" + project.Id;
        if (Translations.TryGetValue(key, out var previous) && previous.IsCompletedSuccessfully && previous.Result is null) Translations.TryRemove(key, out _);
        return Translations.GetOrAdd(key, _ => TranslateAsync(name, project));
    }

    private static async Task<McModTranslation?> TranslateAsync(string name, ModProject? project)
    {
        await Enrichment.WaitAsync();
        try
        {
            if (!IsTranslationEnabled) return null;
            string path = LauncherCache.Shared.Community("translations-v2", project is null ? name : project.Platform + ":" + project.Id, ".json");
            if (File.Exists(path) && !CacheFileTree.IsLinked(path))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<McModTranslation?>(await File.ReadAllBytesAsync(path));
                    var lifetime = cached is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromDays(7);
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < lifetime) return cached;
                }
                catch (Exception error) when (error is JsonException or FileNotFoundException or DirectoryNotFoundException)
                {
                    // 读取期间被回收或内容失效时重新获取.
                }
            }

            var result = await Task.Run(() => new McModTranslationClient(Http).FindAsync(name,
                project?.Platform == ModPlatform.CurseForge ? project.Slug : null,
                project?.Platform == ModPlatform.Modrinth ? project.Slug : null)
            );
            await LauncherCache.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(result));
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or TaskCanceledException)
        {
            App.Logger.Error("Mods", $"MC 百科译名暂不可用: {name}.", error);
            return null;
        }
        finally
        {
            Enrichment.Release();
        }
    }

    internal static Task<ImageSource?> ImageAsync(Uri? uri)
    {
        if (uri is null) return Task.FromResult<ImageSource?>(null);
        if (Images.TryGetValue(uri.AbsoluteUri, out var previous) && previous.IsCompletedSuccessfully && previous.Result is null) Images.TryRemove(uri.AbsoluteUri, out _);
        // WIC 管线要求 MTA, 并将图片解码与缩放留在后台, 不占用界面线程.
        return Images.GetOrAdd(uri.AbsoluteUri, _ => Task.Run(() => LoadImageAsync(uri)));
    }

    private static async Task<ImageSource?> LoadImageAsync(Uri uri)
    {
        try
        {
            string path = LauncherCache.Shared.Community("icons", uri.AbsoluteUri, ".png");
            if (File.Exists(path) && !CacheFileTree.IsLinked(path))
            {
                try
                {
                    var cached = Decode(await File.ReadAllBytesAsync(path));
                    if (cached is not null)
                    {
                        LauncherCache.Touch(path);
                        return cached;
                    }
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { /* 已回收的图标重新下载. */ }
            }

            byte[]? bytes = await DownloadHttpPolicy.ExecuteAsync(token => FetchIconAsync(uri, token), LauncherDownloadPolicy.Capture());
            var image = Decode(bytes);
            if (image is BitmapSource bitmap)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var saved = new MemoryStream();
                encoder.Save(saved);
                try { await LauncherCache.WriteAsync(path, saved.ToArray()); }
                catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
                {
                    App.Logger.Error("Cache", $"无法保存已下载的 Mods 图标: {uri.Host}.", error);
                }
            }

            return image;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException or ArgumentException or NotSupportedException)
        {
            App.Logger.Error("Mods", $"Mods 图标暂不可用: {uri.Host}.", error);
            return null;
        }
    }

    private static async Task<byte[]?> FetchIconAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("ELCL/" + Core.CoreInfo.Version);
        using var response = await IconHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) return null;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > 4 * 1024 * 1024) return null;
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    internal static ImageSource? Decode(byte[]? bytes) => ModIconDecoder.Decode(bytes);


    internal static string FileName(ModFile file, string? translated)
    {
        string original = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(original) || original != file.FileName || original.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || original.Length > 220 || !(original.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || original.EndsWith(".litemod", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(L("invalidFile"));
        ModFileNameFormat format = ModLocalization.FileNameFormat(App.Settings);
        if (string.IsNullOrWhiteSpace(translated) || format == ModFileNameFormat.Original) return original;
        string name = string.Concat(translated.Select(value => Path.GetInvalidFileNameChars().Contains(value) ? '_' : value));
        name = name[..Math.Min(48, name.Length)].Trim().TrimEnd('.');
        string stem = Path.GetFileNameWithoutExtension(original), extension = Path.GetExtension(original);

        string Format(string value) => format switch
        {
            ModFileNameFormat.ChineseBrackets => $"【{name}】 {value}{extension}",
            ModFileNameFormat.SquareBrackets => $"[{name}] {value}{extension}",
            ModFileNameFormat.NamePrefix => $"{name}-{value}{extension}",
            ModFileNameFormat.NameSuffix => $"{value}-{name}{extension}",
            _ => original
        };

        string result = Format(stem);
        return result.Length <= 240 ? result : Format(stem[..Math.Max(1, stem.Length - (result.Length - 240))]);
    }

    internal static void Open(string target, bool reveal = false)
    {
        try
        {
            Process.Start(reveal
                ? new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"") { UseShellExecute = true }
                : new ProcessStartInfo(target) { UseShellExecute = true });
            App.Logger.Information("Mods", $"打开 Mods 位置或页面: {target}.");
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidDataException or InvalidOperationException)
        {
            App.Logger.Error("Mods", "无法打开 Mods 位置或页面.", error);
            LauncherToasts.Error(L("openFailed"));
        }
    }
}

public sealed class ModProjectRow : INotifyPropertyChanged
{
    internal ModProject Project { get; }
    internal McModTranslation? Translation { get; private set; }
    public string Name => CommunityMods.IsTranslationEnabled && Translation is { ChineseName.Length: > 0 } translated ? translated.ChineseName : Project.Name;
    public string OriginalName => Name == Project.Name ? "" : Project.Name;
    public string OriginalNameLabel => OriginalName.Length == 0 ? "" : "  |  " + OriginalName;
    public string VersionLabel => "";
    public string HeadingToolTip => Name == Project.Name ? Name : Name + " | " + Project.Name;
    public string Summary => Project.Summary;
    public Uri WikiUri => Translation?.Website ?? new Uri("https://search.mcmod.cn/s?filter=1&key=" + Uri.EscapeDataString(Project.Name));
    public string WikiHint => CommunityMods.L(Translation is null ? "searchWiki" : "openWiki");
    public System.Windows.Visibility WikiVisibility => Project.Kind == ModProjectKind.Mod && ModLocalization.SupportsLanguage(App.Settings.Language)
        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public IReadOnlyList<string> CategoryLabels => Project.Categories.Select(ModPresentation.CategoryName).Distinct().Take(3).ToArray();
    public Geometry KindIcon => CommunityResourceCatalog.Icon(Project.Kind);
    public string LoadersText => !CommunityResourceCatalog.HasLoaderFilter(Project.Kind)
        ? LocalizationManager.Get(CommunityResourceCatalog.TitleKey(Project.Kind)) : Project.Loaders.Count == 0
        ? CommunityMods.L("anyLoader") : string.Join(" / ", Project.Loaders.OrderBy(ModPresentation.LoaderOrder).Select(ModPresentation.LoaderName));
    public string PlatformText => Project.Platform.ToString();

    public string DownloadCountText => Project.Downloads >= 100000000 ? string.Format(CultureInfo.CurrentCulture, CommunityMods.L("downloadsHundredMillion"), Project.Downloads / 100000000.0)
        : Project.Downloads >= 10000 ? string.Format(CultureInfo.CurrentCulture, CommunityMods.L("downloadsTenThousand"), Project.Downloads / 10000.0) : Project.Downloads.ToString("N0", CultureInfo.CurrentCulture);

    public string UpdatedText
    {
        get
        {
            if (Project.Updated is not { } updated) return CommunityMods.L("updatedUnknown");
            var age = DateTimeOffset.Now - updated;
            return age.TotalMinutes < 1 ? CommunityMods.L("updatedNow")
                : age.TotalHours < 1 ? string.Format(CommunityMods.L("updatedMinutes"), (int)age.TotalMinutes)
                : age.TotalDays < 1 ? string.Format(CommunityMods.L("updatedHours"), (int)age.TotalHours)
                : age.TotalDays < 365 ? string.Format(CommunityMods.L("updatedDays"), (int)age.TotalDays) : updated.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        }
    }

    public string UpdatedToolTip => Project.Updated?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? CommunityMods.L("updatedUnknown");
    public string Metadata => $"{Project.Platform} · {string.Format(CommunityMods.L("downloads"), Project.Downloads)}";
    public ImageSource? Icon { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private Task? _iconEnrichment;
    private int _translationVersion;
    internal ModProjectRow(ModProject project) => Project = project;
    internal Task EnrichAsync() => Task.WhenAll(_iconEnrichment ??= LoadIconAsync(), LoadTranslationAsync());

    private async Task LoadIconAsync()
    {
        Icon = await CommunityMods.ImageAsync(Project.Icon);
        PropertyChanged?.Invoke(this, new(nameof(Icon)));
    }

    private async Task LoadTranslationAsync()
    {
        int version = ++_translationVersion;
        var translation = await CommunityMods.TranslationAsync(Project.Name, Project);
        if (version != _translationVersion) return;
        Translation = translation;
        PropertyChanged?.Invoke(this, new(nameof(Name)));
        PropertyChanged?.Invoke(this, new(nameof(OriginalName)));
        PropertyChanged?.Invoke(this, new(nameof(OriginalNameLabel)));
        PropertyChanged?.Invoke(this, new(nameof(HeadingToolTip)));
        PropertyChanged?.Invoke(this, new(nameof(WikiUri)));
        PropertyChanged?.Invoke(this, new(nameof(WikiHint)));
        PropertyChanged?.Invoke(this, new(nameof(WikiVisibility)));
    }
}
