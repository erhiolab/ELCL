using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Icons;

/// <summary>集中保存启动器界面使用的字体图标和矢量图标.</summary>
public static class LauncherIcons
{
    /// <summary>字体图标统一使用的字形字体.</summary>
    public static readonly FontFamily GlyphFont = new("Segoe MDL2 Assets");

    /// <summary>顶部导航的启动图标.</summary>
    public const string Launch = "\uE768";

    /// <summary>顶部导航的下载图标.</summary>
    public const string Download = "\uE896";

    /// <summary>版本搜索框图标.</summary>
    public const string Search = "\uE721";

    /// <summary>顶部导航的设置图标.</summary>
    public const string Settings = "\uE713";

    /// <summary>窗口最小化图标.</summary>
    public const string Minimize = "\uE921";

    /// <summary>窗口最大化图标.</summary>
    public const string Maximize = "\uE922";

    /// <summary>窗口还原图标.</summary>
    public const string Restore = "\uE923";

    /// <summary>窗口关闭图标.</summary>
    public const string Close = "\uE8BB";

    /// <summary>侧栏重置按钮图标.</summary>
    public const string Reset = "\uE777";

    /// <summary>白昼模式图标.</summary>
    public const string DayTheme = "\uE706";

    /// <summary>深夜模式图标.</summary>
    public const string NightTheme = "\uE708";

    /// <summary>未登录用户占位图标.</summary>
    public const string User = "\uE77B";

    /// <summary>顶部导航的四宫格图标.</summary>
    public static readonly Geometry More = Parse("M 0,0 H 5 V 5 H 0 Z M 8,0 H 13 V 5 H 8 Z M 0,8 H 5 V 13 H 0 Z M 8,8 H 13 V 13 H 8 Z");

    /// <summary>游戏版本列表图标.</summary>
    public static readonly Geometry GameVersions = Parse("M 3,3 L 9,1 L 15,3 L 15,16 L 9,18 L 3,16 Z M 9,1 L 9,18 M 3,7 L 9,9 L 15,7");

    /// <summary>正式版的方块图标.</summary>
    public static readonly Geometry VersionRelease = Parse("M 9,1 L 16,5 L 16,13 L 9,17 L 2,13 L 2,5 Z M 2,5 L 9,9 L 16,5 M 9,9 L 9,17");

    /// <summary>预览版的试验烧瓶图标.</summary>
    public static readonly Geometry VersionPreview = Parse("M 6,2 L 12,2 M 8,2 L 8,8 L 3,15 Q 2,17 5,17 L 13,17 Q 16,17 15,15 L 10,8 L 10,2 M 5,13 L 13,13");

    /// <summary>远古版的砖纹图标.</summary>
    public static readonly Geometry VersionAncient = Parse("M 2,3 L 16,3 L 16,16 L 2,16 Z M 2,9 L 16,9 M 9,3 L 9,9 M 6,9 L 6,16 M 13,9 L 13,16");

    /// <summary>愚人节版的星芒图标.</summary>
    public static readonly Geometry VersionAprilFools = Parse("M 9,1 L 11,7 L 17,9 L 11,11 L 9,17 L 7,11 L 1,9 L 7,7 Z");

    /// <summary>下载设置侧栏的箭头图标.</summary>
    public static readonly Geometry DownloadSettings = Parse("M 9,2 L 9,12 M 5,8 L 9,12 L 13,8 M 3,14 L 3,16 L 15,16 L 15,14");

    /// <summary>Mod 和资源图标.</summary>
    public static readonly Geometry ModsAndResources = Parse("M 7,2 L 11,2 L 11,6 L 15,6 L 15,10 L 11,10 L 11,14 L 7,14 L 7,10 L 3,10 L 3,6 L 7,6 Z");

    /// <summary>整合包图标.</summary>
    public static readonly Geometry Modpacks = Parse("M 2,5 L 9,2 L 16,5 L 9,8 Z M 2,9 L 9,12 L 16,9 M 2,13 L 9,16 L 16,13");

    /// <summary>个性化图标.</summary>
    public static readonly Geometry Personalization = Parse("M 5,2 L 8,1 L 10,4 L 12,1 L 15,2 L 18,6 L 15,8 L 14,6 L 14,17 L 6,17 L 6,6 L 5,8 L 2,6 Z");

    /// <summary>调试侧栏的终端图标.</summary>
    public static readonly Geometry Debug = Parse("M 2,3 L 16,3 L 16,15 L 2,15 Z M 5,7 L 8,9 L 5,11 M 10,12 L 13,12");

    /// <summary>帮助图标.</summary>
    public static readonly Geometry Help = Parse("M 9,2 A 7,7 0 1 1 9,16 A 7,7 0 1 1 9,2 M 7,7 A 2,2 0 0 1 11,7 C 11,9 9,9 9,11 M 9,13 L 9,13.2");

    /// <summary>关于图标.</summary>
    public static readonly Geometry About = Parse("M 9,2 A 7,7 0 1 1 9,16 A 7,7 0 1 1 9,2 M 9,8 L 9,13 M 9,5 L 9,5.2");

    private static Geometry Parse(string path)
    {
        Geometry geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
}
