using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher.Controls;

public sealed class LauncherBackgroundPresenter : Grid
{
    private readonly Grid _surface = new();
    private readonly BlurEffect _blur = new() { Radius = 0, RenderingBias = RenderingBias.Performance };
    private readonly DispatcherTimer _videoTimeout = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _slideshow = new();
    private LauncherBackgroundFile[] _catalog = [];
    private LauncherBackgroundFile[] _wallpaperFiles = [];
    private LauncherBackgroundFile[] _automaticFiles = [];
    private LauncherBackgroundFile[] _randomOrder = [];
    private LauncherBackgroundFile[] _files = [];
    private bool _localCatalogReady;
    private bool _wallpaperCatalogReady;
    private LauncherBackgroundSettings _options = new();
    private MediaElement? _video;
    private Image? _image;
    private int _index;
    private int _generation;
    private bool _playbackActive;

    internal event Action? BackgroundChanged;
    internal event Action<LauncherBackgroundFile, Exception>? MediaRejected;
    internal event Action<LauncherBackgroundFile>? MediaReady;
    internal LauncherBackgroundFile? DisplayedFile { get; private set; }
    internal BackgroundSlideshowPosition SlideshowPosition { get; set; } = new();
    internal bool IsDisplayingBackground => DisplayedFile is not null;
    internal bool CanAdvance => _options.Enabled && _options.PlaybackMode != BackgroundPlaybackMode.Fixed && _automaticFiles.Length > 1;

    public LauncherBackgroundPresenter()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        Children.Add(_surface);
        _slideshow.Tick += (_, _) => Advance();
        _videoTimeout.Tick += (_, _) =>
        {
            if (_video is not null) RejectCurrent(new TimeoutException("背景视频打开超时."));
        };
    }

    internal void SetFiles(LauncherBackgroundFile[] files)
    {
        _catalog = files;
        _localCatalogReady = true;
        if (_options.ActiveCategory != BackgroundCategory.Wallpaper)
            SelectMedia(keepCurrent: true);
    }

    internal void SetWallpaperFiles(LauncherBackgroundFile[] files)
    {
        _wallpaperFiles = files;
        _wallpaperCatalogReady = true;
        if (_options.NeedsWallpaperCatalog) SelectMedia(keepCurrent: true);
    }

    internal void Advance()
    {
        if (CanAdvance) SelectMedia(keepCurrent: false, step: 1);
    }

    internal void GoBack()
    {
        if (CanAdvance) SelectMedia(keepCurrent: false, step: -1);
    }

    internal bool TryShowFile(LauncherBackgroundFile file) => _options.PlaybackMode != BackgroundPlaybackMode.Fixed
        && SelectMedia(keepCurrent: true, requestedKey: file.FavoriteKey);

    private LauncherBackgroundFile[] ResolveAutomaticFiles(bool waitForCatalogs)
    {
        if (_options.ActiveCategory != BackgroundCategory.Favorites) return _catalog;
        // 随机和轮播都应等待所需的图库就绪, 避免启动时只从先加载的来源中选取.
        if (waitForCatalogs && ((_options.HasLocalFavorites && !_localCatalogReady) || (_options.HasWallpaperFavorites && !_wallpaperCatalogReady))) return [];
        return _options.ResolveFavorites(_catalog, _wallpaperFiles);
    }

    private bool SelectMedia(bool keepCurrent, string? requestedKey = null, int step = 0)
    {
        var displayed = DisplayedFile;
        // 刷新图库时保留正在载入的素材, 也允许用户先播放已加载的收藏.
        var current = keepCurrent || step != 0 ? displayed ?? _files.ElementAtOrDefault(_index) : null;
        if (step == 0)
        {
            var automaticFiles = _options.PlaybackMode == BackgroundPlaybackMode.Fixed ? []
                : ResolveAutomaticFiles(waitForCatalogs: requestedKey is null && (!keepCurrent || current is null));
            if (requestedKey is not null)
            {
                current = automaticFiles.FirstOrDefault(file => string.Equals(file.FavoriteKey, requestedKey, StringComparison.OrdinalIgnoreCase));
                if (current is null) return false;
            }
            // 随机模式在同一轮内保持顺序, 上一张可以回到刚才看过的素材.
            if (_options.PlaybackMode == BackgroundPlaybackMode.RandomOnStartup
                && (!keepCurrent || !_automaticFiles.SequenceEqual(automaticFiles)))
            {
                _randomOrder = [.. automaticFiles];
                Random.Shared.Shuffle(_randomOrder);
            }
            _automaticFiles = automaticFiles;
        }
        switch (_options.PlaybackMode)
        {
            case BackgroundPlaybackMode.Fixed:
                var fixedFile = _options.FixedKey is { } key
                    ? _options.ResolveCategory(_catalog, _wallpaperFiles)
                        .FirstOrDefault(file => string.Equals(file.FavoriteKey, key, StringComparison.OrdinalIgnoreCase))
                    : null;
                _files = fixedFile is null ? [] : [fixedFile];
                break;
            default:
                _files = BuildPlaybackOrder(current, step);
                break;
        }

        _index = 0;
        if (keepCurrent && _options.Enabled && displayed is not null && _files.Length != 0 && SameKey(_files[0], displayed)
            && string.Equals(_files[0].Path, displayed.Path, StringComparison.OrdinalIgnoreCase))
        {
            RestartSlideTimer();
            return true;
        }
        Restart();
        return true;
    }

    private LauncherBackgroundFile[] BuildPlaybackOrder(LauncherBackgroundFile? current, int step)
    {
        var slideshow = _options.PlaybackMode == BackgroundPlaybackMode.Slideshow;
        var order = slideshow ? _automaticFiles : _randomOrder;
        var count = order.Length;
        if (count == 0) return [];

        var index = Array.FindIndex(order, file => SameKey(file, current));
        if (index < 0 && step == 0 && slideshow && SlideshowPosition.GetKey(_options.ActiveCategory) is { } key)
            index = Array.FindIndex(order, file => string.Equals(file.FavoriteKey, key, StringComparison.OrdinalIgnoreCase));

        var direction = step < 0 ? -1 : 1;
        var start = index >= 0 ? (index + step + count) % count : direction < 0 ? count - 1 : 0;
        var result = new LauncherBackgroundFile[count];
        for (int i = 0; i < count; i++) result[i] = order[(start + direction * i + count) % count];
        return result;
    }

    private static bool SameKey(LauncherBackgroundFile file, LauncherBackgroundFile? current) =>
        current is not null && string.Equals(file.FavoriteKey, current.FavoriteKey, StringComparison.OrdinalIgnoreCase);

    internal void Apply(LauncherBackgroundSettings options)
    {
        var enabledChanged = options.Enabled != _options.Enabled;
        var categoryChanged = options.ActiveCategory != _options.ActiveCategory;
        var modeChanged = options.PlaybackMode != _options.PlaybackMode;
        var fixedFileChanged = options.PlaybackMode == BackgroundPlaybackMode.Fixed
            && !string.Equals(options.FixedKey, _options.FixedKey, StringComparison.OrdinalIgnoreCase);
        var poolChanged = options.ActiveCategory == BackgroundCategory.Favorites
            && !options.Favorites.SequenceEqual(_options.Favorites, StringComparer.OrdinalIgnoreCase);
        var intervalChanged = options.SlideIntervalSeconds != _options.SlideIntervalSeconds;
        _options = options;
        _surface.Opacity = options.Opacity / 100.0;
        _blur.Radius = options.BlurRadius;
        _surface.Effect = options.BlurRadius == 0 ? null : _blur;
        if (_image is not null) _image.Stretch = MediaStretch;
        if (_video is not null)
        {
            _video.Stretch = MediaStretch;
            _video.Volume = IsDisplayingBackground ? options.Volume / 100.0 : 0;
        }

        if (categoryChanged || modeChanged || fixedFileChanged || poolChanged)
            SelectMedia(keepCurrent: (!categoryChanged && !modeChanged) || options.PlaybackMode == BackgroundPlaybackMode.Fixed);
        else if (enabledChanged) Restart();
        else if (intervalChanged) RestartSlideTimer();
    }

    internal void SetPlaybackActive(bool active)
    {
        if (_playbackActive == active) return;
        _playbackActive = active;
        RestartSlideTimer();
        if (_video is not { } video || !IsDisplayingBackground) return;
        if (active) video.Play();
        else video.Pause();
    }

    internal void Stop()
    {
        _generation++;
        _slideshow.Stop();
        RemoveVideo();
        _image = null;
        _surface.Children.Clear();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsDisplayingBackground) return;
        DisplayedFile = null;
        BackgroundChanged?.Invoke();
    }

    private void RemoveVideo()
    {
        _videoTimeout.Stop();
        var video = _video;
        _video = null;
        if (video is null) return;
        video.Close();
        video.Source = null;
        _surface.Children.Remove(video);
    }

    private Stretch MediaStretch => _options.Fit switch
    {
        BackgroundFit.Contain => Stretch.Uniform,
        BackgroundFit.Stretch => Stretch.Fill,
        _ => Stretch.UniformToFill
    };

    private void Restart()
    {
        Stop();
        if (_options.Enabled) _ = OpenNextAsync(_generation);
    }

    private async Task OpenNextAsync(int generation)
    {
        while (generation == _generation && _index < _files.Length)
        {
            var file = _files[_index];
            try
            {
                if (file.IsVideo)
                {
                    OpenVideo(file);
                    return;
                }

                var dpi = VisualTreeHelper.GetDpi(this);
                var decodeEdge = GetImageDecodeEdge(ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY);
                var bitmap = await Task.Run(() => ReadImage(file.Path, decodeEdge));
                if (generation != _generation) return;
                _image = new Image { Source = bitmap, Stretch = MediaStretch };
                _surface.Children.Add(_image);
                ShowMedia(file);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException or COMException)
            {
                if (generation != _generation) return;
                RemoveVideo();
                MediaRejected?.Invoke(file, error);
                _index++;
            }
        }
    }

    private void OpenVideo(LauncherBackgroundFile file)
    {
        var video = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            Stretch = MediaStretch,
            Volume = 0,
            ScrubbingEnabled = true
        };
        video.MediaOpened += (_, _) =>
        {
            if (!ReferenceEquals(_video, video)) return;
            if (!video.HasVideo)
            {
                RejectCurrent(new NotSupportedException("文件没有可播放的视频轨道."));
                return;
            }

            _videoTimeout.Stop();
            if (!_playbackActive) video.Pause();
            video.Volume = _options.Volume / 100.0;
            ShowMedia(file);
        };
        video.MediaEnded += (_, _) =>
        {
            if (!ReferenceEquals(_video, video)) return;
            video.Position = TimeSpan.Zero;
            if (_playbackActive) video.Play();
        };
        video.MediaFailed += (_, args) =>
        {
            if (ReferenceEquals(_video, video)) RejectCurrent(args.ErrorException);
        };
        _video = video;
        _surface.Children.Add(video);
        _videoTimeout.Start();
        video.Source = new Uri(file.Path, UriKind.Absolute);
        video.Play();
    }

    private void RejectCurrent(Exception error)
    {
        var file = _files[_index++];
        MediaRejected?.Invoke(file, error);
        Restart();
    }

    private void ShowMedia(LauncherBackgroundFile file)
    {
        DisplayedFile = file;
        if (_options.PlaybackMode == BackgroundPlaybackMode.Slideshow)
            SlideshowPosition = SlideshowPosition.WithKey(_options.ActiveCategory, file.FavoriteKey);
        RestartSlideTimer();
        MediaReady?.Invoke(file);
        BackgroundChanged?.Invoke();
        Opacity = 1;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private void RestartSlideTimer()
    {
        _slideshow.Stop();
        if (_playbackActive && IsDisplayingBackground && _options.PlaybackMode == BackgroundPlaybackMode.Slideshow && _automaticFiles.Length > 1)
        {
            _slideshow.Interval = TimeSpan.FromSeconds(_options.SlideIntervalSeconds);
            _slideshow.Start();
        }
    }

    // 按实际像素尺寸解码, 并留出放大窗口的余量, 避免拖动窗口时重新读取文件.
    internal static int GetImageDecodeEdge(double width, double height)
    {
        var longestEdge = Math.Max(width, height);
        if (!double.IsFinite(longestEdge) || longestEdge <= 0) return 1536;
        return (int)Math.Clamp(Math.Ceiling(longestEdge * 1.25 / 256) * 256, 1024, 2560);
    }

    internal static BitmapSource ReadImage(string path, int maxEdge)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var width = header.PixelWidth;
        var height = header.PixelHeight;
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;

        if (width >= height && width > maxEdge) image.DecodePixelWidth = maxEdge;
        else if (height > maxEdge) image.DecodePixelHeight = maxEdge;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
