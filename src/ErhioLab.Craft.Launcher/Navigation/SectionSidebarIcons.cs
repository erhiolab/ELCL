using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>集中保存下载/设置/更多侧栏使用的矢量图标.</summary>
internal static class SectionSidebarIcons
{
    /// <summary>游戏版本列表图标.</summary>
    internal static readonly Geometry GameVersions = Parse(
        "M 3,3 L 9,1 L 15,3 L 15,16 L 9,18 L 3,16 Z M 9,1 L 9,18 M 3,7 L 9,9 L 15,7"
    );

    /// <summary>Mod 和资源图标.</summary>
    internal static readonly Geometry ModsAndResources = Parse(
        "M 7,2 L 11,2 L 11,6 L 15,6 L 15,10 L 11,10 L 11,14 L 7,14 L 7,10 L 3,10 L 3,6 L 7,6 Z"
    );

    /// <summary>整合包图标.</summary>
    internal static readonly Geometry Modpacks = Parse(
        "M 2,5 L 9,2 L 16,5 L 9,8 Z M 2,9 L 9,12 L 16,9 M 2,13 L 9,16 L 16,13"
    );

    /// <summary>个性化图标.</summary>
    internal static readonly Geometry Personalization = Parse(
        "M 5,2 L 8,1 L 10,4 L 12,1 L 15,2 L 18,6 L 15,8 L 14,6 L 14,17 L 6,17 L 6,6 L 5,8 L 2,6 Z"
    );

    /// <summary>帮助图标.</summary>
    internal static readonly Geometry Help = Parse(
        "M 9,2 A 7,7 0 1 1 9,16 A 7,7 0 1 1 9,2 M 7,7 A 2,2 0 0 1 11,7 C 11,9 9,9 9,11 M 9,13 L 9,13.2"
    );

    /// <summary>关于图标.</summary>
    internal static readonly Geometry About = Parse(
        "M 9,2 A 7,7 0 1 1 9,16 A 7,7 0 1 1 9,2 M 9,8 L 9,13 M 9,5 L 9,5.2"
    );

    private static Geometry Parse(string path)
    {
        Geometry geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
}
