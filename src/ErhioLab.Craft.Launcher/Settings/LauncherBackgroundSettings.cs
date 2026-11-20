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

public sealed record LauncherBackgroundSettings
{
    public bool Enabled { get; init; } = true;
    public bool PlayInBackground { get; init; }
    public BackgroundFit Fit { get; init; }
    public int Opacity { get; init; } = 85;
    public int CardOpacity { get; init; } = 85;
    public int BlurRadius { get; init; }
    public int Volume { get; init; }

    public BackgroundPlaybackMode PlaybackMode { get; init; }

    // 顶层文件名本身就是相对路径, 保留同一个 JSON 键.
    [JsonPropertyName("SelectedFileName")] public string? SelectedRelativePath { get; init; }
    public string? SelectedWallpaperId { get; init; }
    public int SlideIntervalSeconds { get; init; } = 60;

    internal LauncherBackgroundSettings Normalize() => this with
    {
        Fit = Enum.IsDefined(Fit) ? Fit : BackgroundFit.Cover,
        Opacity = Math.Clamp(Opacity, 0, 100),
        CardOpacity = Math.Clamp(CardOpacity, 0, 100),
        BlurRadius = Math.Clamp(BlurRadius, 0, 32),
        Volume = Math.Clamp(Volume, 0, 100),
        PlaybackMode = Enum.IsDefined(PlaybackMode) ? PlaybackMode : BackgroundPlaybackMode.RandomOnStartup,
        SelectedRelativePath = LauncherBackgroundFiles.NormalizeRelativePath(SelectedRelativePath),
        SelectedWallpaperId = PlaybackMode == BackgroundPlaybackMode.Fixed && SelectedWallpaperId is { Length: > 0 and <= 100 }
            ? SelectedWallpaperId : null,
        SlideIntervalSeconds = Math.Clamp(SlideIntervalSeconds, 10, 3600)
    };
}
