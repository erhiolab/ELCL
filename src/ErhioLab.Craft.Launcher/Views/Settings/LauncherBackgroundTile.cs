using System.ComponentModel;
using System.IO;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher.Views.Settings;

public sealed class LauncherBackgroundTile : INotifyPropertyChanged
{
    private bool _isDisplayed;
    private bool _isFixed;
    private bool _unavailable;
    private readonly string? _folder;
    internal LauncherBackgroundFile File { get; }
    public string Name { get; }
    public string RelativePath => File.RelativePath;
    public string ToolTip => File.WallpaperId is null ? File.RelativePath : Name;
    public Geometry Icon => File.IsVideo ? LauncherIcons.Video : LauncherIcons.Image;
    public ImageSource? Thumbnail { get; private set; }
    public bool IsCurrent => _isDisplayed || _isFixed;

    public string StatusLabel
    {
        get
        {
            var status = LocalizationManager.Get(_isDisplayed ? "background.playingTile"
                : _isFixed ? "background.fixedTile"
                : _unavailable ? "background.unavailableTile"
                : File.IsPreviewOnly ? "background.staticPreviewTile"
                : File.IsVideo ? "background.videoTile" : "background.imageTile");
            return string.IsNullOrEmpty(_folder) ? status : string.Format(LocalizationManager.Get("background.tileInFolder"), status, _folder);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal LauncherBackgroundTile(LauncherBackgroundFile file)
    {
        File = file;
        Name = file.Title ?? Path.GetFileName(file.Path);
        _folder = file.WallpaperId is null ? Path.GetDirectoryName(file.RelativePath) : null;
    }

    internal void SetThumbnail(ImageSource thumbnail)
    {
        Thumbnail = thumbnail;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
    }

    internal void MarkUnavailable()
    {
        _unavailable = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
    }

    internal void Refresh(bool isDisplayed, bool isFixed)
    {
        var highlighted = IsCurrent;
        _isDisplayed = isDisplayed;
        _isFixed = isFixed;
        if (highlighted != IsCurrent) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
    }
}
