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
    private LauncherBackgroundFile[] _files = [];
    private LauncherBackgroundSettings _options = new();
    private MediaElement? _video;
    private Image? _image;
    private int _index;
    private int _generation;
    private bool _minimized;

    internal event Action? BackgroundChanged;
    internal event Action<LauncherBackgroundFile, Exception>? MediaRejected;
    internal event Action<LauncherBackgroundFile>? MediaReady;
    internal LauncherBackgroundFile? DisplayedFile { get; private set; }
    internal bool IsDisplayingBackground => DisplayedFile is not null;

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
        if (!UsesWallpaper(_options)) SelectMedia(DisplayedFile?.Path, advance: false);
    }

    internal void SetWallpaperFiles(LauncherBackgroundFile[] files)
    {
        _wallpaperFiles = files;
        if (UsesWallpaper(_options)) SelectMedia(null, advance: false);
    }

    private static bool UsesWallpaper(LauncherBackgroundSettings options) =>
        options.PlaybackMode == BackgroundPlaybackMode.Fixed && options.SelectedWallpaperId is not null;

    internal void Advance()
    {
        if (!_options.Enabled || _options.PlaybackMode == BackgroundPlaybackMode.Fixed || _catalog.Length < 2) return;
        SelectMedia(DisplayedFile?.Path, advance: true);
    }

    private void SelectMedia(string? previousPath, bool advance)
    {
        switch (_options.PlaybackMode)
        {
            case BackgroundPlaybackMode.Fixed:
                var fixedFile = UsesWallpaper(_options)
                    ? _wallpaperFiles.FirstOrDefault(file => string.Equals(file.WallpaperId, _options.SelectedWallpaperId, StringComparison.OrdinalIgnoreCase))
                    : _catalog.FirstOrDefault(file => string.Equals(file.RelativePath, _options.SelectedRelativePath, StringComparison.OrdinalIgnoreCase));
                _files = fixedFile is null ? [] : [fixedFile];
                break;
            case BackgroundPlaybackMode.Slideshow:
                var previous = Array.FindIndex(_catalog, file => string.Equals(file.Path, previousPath, StringComparison.OrdinalIgnoreCase));
                var start = previous < 0 ? 0 : previous + (advance ? 1 : 0);
                _files = [.. _catalog.Skip(start), .. _catalog.Take(start)];
                break;
            default:
                _files = [.. _catalog];
                Random.Shared.Shuffle(_files);
                if (_files.Length > 1 && string.Equals(previousPath, _files[0].Path, StringComparison.OrdinalIgnoreCase))
                {
                    var other = Random.Shared.Next(1, _files.Length);
                    (_files[0], _files[other]) = (_files[other], _files[0]);
                }

                break;
        }

        _index = 0;
        Restart();
    }

    internal void Apply(LauncherBackgroundSettings options)
    {
        var enabledChanged = options.Enabled != _options.Enabled;
        var modeChanged = options.PlaybackMode != _options.PlaybackMode || options.PlaybackMode == BackgroundPlaybackMode.Fixed
            && (!string.Equals(options.SelectedRelativePath, _options.SelectedRelativePath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(options.SelectedWallpaperId, _options.SelectedWallpaperId, StringComparison.OrdinalIgnoreCase));
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

        if (modeChanged) SelectMedia(DisplayedFile?.Path, advance: false);
        else if (enabledChanged) Restart();
        else if (intervalChanged) RestartSlideTimer();
    }

    internal void SetMinimized(bool minimized)
    {
        _minimized = minimized;
        RestartSlideTimer();
        if (_video is not { } video || !IsDisplayingBackground) return;
        if (minimized) video.Pause();
        else video.Play();
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

                var bitmap = await Task.Run(() => ReadImage(file.Path));
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
            video.Volume = _options.Volume / 100.0;
            if (_minimized) video.Pause();
            ShowMedia(file);
        };
        video.MediaEnded += (_, _) =>
        {
            if (!ReferenceEquals(_video, video)) return;
            video.Position = TimeSpan.Zero;
            if (!_minimized) video.Play();
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
        if (!_minimized && IsDisplayingBackground && _options.PlaybackMode == BackgroundPlaybackMode.Slideshow && _catalog.Length > 1)
        {
            _slideshow.Interval = TimeSpan.FromSeconds(_options.SlideIntervalSeconds);
            _slideshow.Start();
        }
    }

    internal static BitmapSource ReadImage(string path, int maxEdge = 2560)
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
