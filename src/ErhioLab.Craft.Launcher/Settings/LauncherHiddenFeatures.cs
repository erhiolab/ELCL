namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>可从界面隐藏的功能. 已分配的位用于配置文件, 不重复使用.</summary>
[Flags]
public enum LauncherHiddenFeatures
{
    None = 0,
    Download = 1 << 0,
    Settings = 1 << 1,
    More = 1 << 2,
    LaunchSettings = 1 << 3,
    Personalization = 1 << 4,
    DownloadSettings = 1 << 5,
    OtherSettings = 1 << 6,
    Debug = 1 << 7,
    About = 1 << 8,
    Toolbox = 1 << 9,
    Performance = 1 << 10,
    VersionManagement = 1 << 11,
    FeatureVisibility = 1 << 12,

    All = Download | Settings | More | LaunchSettings | Personalization | DownloadSettings | OtherSettings | Debug
          | About | Toolbox | Performance | VersionManagement | FeatureVisibility
}
