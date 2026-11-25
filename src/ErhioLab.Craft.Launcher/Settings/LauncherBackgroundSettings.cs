using System.Text.Json.Serialization;

using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher.Settings;

public enum BackgroundFit
{
    Cover,
    Contain,
    Stretch
}

public enum BackgroundPlaybackMode
{
    RandomOnStartup,
    Fixed,
    Slideshow
}

public enum BackgroundCategory
{
    Local,
    Favorites,
    Wallpaper
}

public sealed record LauncherBackgroundSettings
{
    internal const int MinimumIntervalSeconds = 10;
    internal const int MaximumIntervalSeconds = 3600;

    public bool Enabled { get; init; } = true;
    public bool PlayInBackground { get; init; }
    public BackgroundFit Fit { get; init; }
    public int Opacity { get; init; } = 85;
    public int CardOpacity { get; init; } = 85;
    public int NavigationOpacity { get; init; } = 90;
    public int BlurRadius { get; init; }
    public int Volume { get; init; }

    public BackgroundCategory? Category { get; init; }
    [JsonPropertyName("PlaybackMode")] public BackgroundPlaybackMode LocalPlaybackMode { get; init; }
    public BackgroundPlaybackMode FavoritePlaybackMode { get; init; }

    // 顶层文件名本身就是相对路径, 保留同一个 JSON 键.
    [JsonPropertyName("SelectedFileName")] public string? SelectedRelativePath { get; init; }
    public string? SelectedWallpaperId { get; init; }
    public string? SelectedFavoriteKey { get; init; }
    public int SlideIntervalSeconds { get; init; } = 60;
    public string[] Favorites { get; init; } = [];

    internal BackgroundCategory ActiveCategory => Category is { } category && Enum.IsDefined(category) ? category
        : LocalPlaybackMode == BackgroundPlaybackMode.Fixed && HasValidWallpaperId ? BackgroundCategory.Wallpaper : BackgroundCategory.Local;
    internal BackgroundPlaybackMode PlaybackMode => ActiveCategory switch
    {
        BackgroundCategory.Wallpaper => BackgroundPlaybackMode.Fixed,
        BackgroundCategory.Favorites => FavoritePlaybackMode,
        _ => LocalPlaybackMode
    };
    internal string? FixedKey => ActiveCategory switch
    {
        BackgroundCategory.Wallpaper => SelectedWallpaperId is { } id ? LauncherBackgroundFiles.WallpaperPrefix + id : null,
        BackgroundCategory.Favorites => SelectedFavoriteKey,
        _ => SelectedRelativePath is { } path ? LauncherBackgroundFiles.LocalPrefix + path : null
    };
    private bool HasValidWallpaperId => SelectedWallpaperId is { Length: > 0 and <= 100 } && !string.IsNullOrWhiteSpace(SelectedWallpaperId);
    internal bool HasLocalFavorites => Favorites.Any(key => key.StartsWith(LauncherBackgroundFiles.LocalPrefix, StringComparison.OrdinalIgnoreCase));
    internal bool HasWallpaperFavorites => Favorites.Any(key => key.StartsWith(LauncherBackgroundFiles.WallpaperPrefix, StringComparison.OrdinalIgnoreCase));
    internal bool NeedsWallpaperCatalog => ActiveCategory == BackgroundCategory.Wallpaper
        || ActiveCategory == BackgroundCategory.Favorites && HasWallpaperFavorites;

    internal LauncherBackgroundSettings WithMode(BackgroundPlaybackMode mode) => ActiveCategory switch
    {
        BackgroundCategory.Local => this with { LocalPlaybackMode = mode },
        BackgroundCategory.Favorites => this with { FavoritePlaybackMode = mode },
        _ => this
    };

    internal LauncherBackgroundSettings WithFixedFile(LauncherBackgroundFile file) => ActiveCategory switch
    {
        BackgroundCategory.Local => this with { LocalPlaybackMode = BackgroundPlaybackMode.Fixed, SelectedRelativePath = file.RelativePath },
        BackgroundCategory.Favorites => this with { FavoritePlaybackMode = BackgroundPlaybackMode.Fixed, SelectedFavoriteKey = file.FavoriteKey },
        _ => this with { SelectedWallpaperId = file.WallpaperId }
    };

    internal LauncherBackgroundFile[] ResolveCategory(LauncherBackgroundFile[] local, LauncherBackgroundFile[] wallpaper) => ActiveCategory switch
    {
        BackgroundCategory.Favorites => ResolveFavorites(local, wallpaper),
        BackgroundCategory.Wallpaper => wallpaper,
        _ => local
    };

    internal LauncherBackgroundFile[] ResolveFavorites(LauncherBackgroundFile[] local, LauncherBackgroundFile[] wallpaper)
    {
        if (Favorites.Length == 0) return [];
        var files = new Dictionary<string, LauncherBackgroundFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in local.Concat(wallpaper)) files.TryAdd(file.FavoriteKey, file);
        return [.. Favorites.Where(files.ContainsKey).Select(key => files[key])];
    }

    private string[] NormalizeFavorites()
    {
        var source = Favorites ?? [];
        var normalized = source.Select(LauncherBackgroundFiles.NormalizeFavoriteKey).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return source.SequenceEqual(normalized) ? source : normalized;
    }

    internal LauncherBackgroundSettings Normalize() => this with
    {
        Fit = Enum.IsDefined(Fit) ? Fit : BackgroundFit.Cover,
        Opacity = Math.Clamp(Opacity, 0, 100),
        CardOpacity = Math.Clamp(CardOpacity, 0, 100),
        NavigationOpacity = Math.Clamp(NavigationOpacity, 0, 100),
        BlurRadius = Math.Clamp(BlurRadius, 0, 32),
        Volume = Math.Clamp(Volume, 0, 100),
        Category = ActiveCategory,
        LocalPlaybackMode = Enum.IsDefined(LocalPlaybackMode) ? LocalPlaybackMode : BackgroundPlaybackMode.RandomOnStartup,
        FavoritePlaybackMode = Enum.IsDefined(FavoritePlaybackMode) ? FavoritePlaybackMode : BackgroundPlaybackMode.RandomOnStartup,
        SelectedRelativePath = LauncherBackgroundFiles.NormalizeRelativePath(SelectedRelativePath),
        SelectedWallpaperId = HasValidWallpaperId ? SelectedWallpaperId : null,
        SelectedFavoriteKey = LauncherBackgroundFiles.NormalizeFavoriteKey(SelectedFavoriteKey),
        SlideIntervalSeconds = Math.Clamp(SlideIntervalSeconds, MinimumIntervalSeconds, MaximumIntervalSeconds),
        Favorites = NormalizeFavorites()
    };
}
