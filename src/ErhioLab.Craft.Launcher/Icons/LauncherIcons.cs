using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Icons;

/// <summary>集中保存启动器界面使用的线框图标.</summary>
public static class LauncherIcons
{
    /// <summary>系统性能.</summary>
    public static readonly Geometry Performance = Parse("M 2,15 V 9 H 5 V 15 Z M 8,15 V 5 H 11 V 15 Z M 14,15 V 1 H 17 V 15 Z M 1,18 H 18");
    /// <summary>处理器.</summary>
    public static readonly Geometry Processor = Parse("M 4,4 H 14 V 14 H 4 Z M 7,7 H 11 V 11 H 7 Z M 6,1 V 4 M 9,1 V 4 M 12,1 V 4 M 6,14 V 17 M 9,14 V 17 M 12,14 V 17 M 1,6 H 4 M 1,9 H 4 M 1,12 H 4 M 14,6 H 17 M 14,9 H 17 M 14,12 H 17");
    /// <summary>物理内存.</summary>
    public static readonly Geometry Memory = Parse("M 2,4 H 16 V 13 H 2 Z M 5,7 V 10 M 9,7 V 10 M 13,7 V 10 M 4,13 V 16 M 7,13 V 16 M 11,13 V 16 M 14,13 V 16");
    /// <summary>显示适配器.</summary>
    public static readonly Geometry Graphics = Parse("M 2,4 H 16 V 13 H 2 Z M 2,2 V 16 M 5,13 V 16 H 12 V 13 M 11,8.5 A 3,3 0 1 1 5,8.5 A 3,3 0 1 1 11,8.5 M 14,7 V 10");
    /// <summary>磁盘容量.</summary>
    public static readonly Geometry Disk = Parse("M 4,3 H 14 L 17,11 V 15 H 1 V 11 Z M 1,11 H 17 M 12,13 H 12.2 M 14.5,13 H 14.7");
    /// <summary>操作系统.</summary>
    public static readonly Geometry OperatingSystem = Parse("M 2,3 H 16 V 15 H 2 Z M 2,7 H 16 M 5,5 H 5.2 M 8,5 H 8.2");
    /// <summary>启动.</summary>
    public static readonly Geometry Launch = Parse("M 4,2 L 11,6.3 Q 11.8,7 11,7.7 L 4,12 Q 3,12.5 3,11.5 L 3,2.5 Q 3,1.5 4,2 Z");

    /// <summary>下载.</summary>
    public static readonly Geometry Download = Parse("M 9,2 L 9,12 M 5,8 L 9,12 L 13,8 M 3,14 L 3,16 L 15,16 L 15,14");

    /// <summary>搜索.</summary>
    public static readonly Geometry Search = Parse("M 11,11 L 16,16 M 13,7 A 6,6 0 1 1 1,7 A 6,6 0 1 1 13,7");

    /// <summary>返回.</summary>
    public static readonly Geometry Back = Parse("M 9,2 L 3,8 L 9,14 M 3,8 L 16,8");

    /// <summary>设置.</summary>
    public static readonly Geometry Settings = Parse("M 9,2 L 10,3.5 L 12,3 L 14,5 L 13.5,7 L 15,8 L 15,11 L 13.5,12 L 14,14 L 12,16 L 10,15.5 L 9,17 L 6,17 L 5,15.5 L 3,16 L 1,14 L 1.5,12 L 0,11 L 0,8 L 1.5,7 L 1,5 L 3,3 L 5,3.5 L 6,2 Z M 11,9.5 A 3.5,3.5 0 1 1 4,9.5 A 3.5,3.5 0 1 1 11,9.5");

    /// <summary>窗口最小化.</summary>
    public static readonly Geometry Minimize = Parse("M 2,9 L 16,9");

    /// <summary>窗口最大化.</summary>
    public static readonly Geometry Maximize = Parse("M 2,2 L 16,2 L 16,16 L 2,16 Z");

    /// <summary>窗口还原.</summary>
    public static readonly Geometry Restore = Parse("M 5,5 L 16,5 L 16,16 L 5,16 Z M 2,12 L 2,2 L 12,2 L 12,5");

    /// <summary>关闭.</summary>
    public static readonly Geometry Close = Parse("M 3,3 L 15,15 M 15,3 L 3,15");

    /// <summary>重置.</summary>
    public static readonly Geometry Reset = Parse("M 3,7 A 6,6 0 1 1 5,13 M 3,3 L 3,7 L 7,7");

    /// <summary>展开或收起内容, 旋转后指向上方.</summary>
    public static readonly Geometry ChevronDown = Parse("M 3,6 L 9,12 L 15,6");

    /// <summary>白昼模式.</summary>
    public static readonly Geometry DayTheme = Parse("M 9,1 L 9,3 M 9,15 L 9,17 M 1,9 L 3,9 M 15,9 L 17,9 M 3,3 L 4.5,4.5 M 13.5,13.5 L 15,15 M 15,3 L 13.5,4.5 M 4.5,13.5 L 3,15 M 13,9 A 4,4 0 1 1 5,9 A 4,4 0 1 1 13,9");

    /// <summary>深夜模式.</summary>
    public static readonly Geometry NightTheme = Parse("M 13,2 A 7,7 0 1 0 16,13 A 7,7 0 0 1 13,2 Z");

    /// <summary>用户.</summary>
    public static readonly Geometry User = Parse("M 12,5 A 4,4 0 1 1 4,5 A 4,4 0 1 1 12,5 M 1,17 Q 1,11 8,11 Q 15,11 15,17");

    /// <summary>删除.</summary>
    public static readonly Geometry Delete = Parse("M 3,5 L 15,5 M 6,5 L 6,3 L 12,3 L 12,5 M 5,5 L 6,16 L 12,16 L 13,5 M 8,8 L 8,13 M 10,8 L 10,13");

    /// <summary>提示信息.</summary>
    public static readonly Geometry Info = Parse("M 9,2 A 7,7 0 1 1 9,16 A 7,7 0 1 1 9,2 M 9,8 L 9,13 M 9,5 L 9,5.2");

    /// <summary>错误.</summary>
    public static readonly Geometry Error = Parse("M 9,2 A 7,7 0 1 1 9,16 A 7,7 0 1 1 9,2 M 6.5,6.5 L 11.5,11.5 M 11.5,6.5 L 6.5,11.5");

    /// <summary>已选中.</summary>
    public static readonly Geometry Check = Parse("M 3,9 L 7,13 L 15,4");

    /// <summary>顶部导航的四宫格图标.</summary>
    public static readonly Geometry More = Parse("M 0,0 H 5 V 5 H 0 Z M 8,0 H 13 V 5 H 8 Z M 0,8 H 5 V 13 H 0 Z M 8,8 H 13 V 13 H 8 Z");

    /// <summary>游戏版本列表图标.</summary>
    public static readonly Geometry GameVersions = Parse("M 3,3 L 9,1 L 15,3 L 15,16 L 9,18 L 3,16 Z M 9,1 L 9,18 M 3,7 L 9,9 L 15,7");

    /// <summary>正式版的方块图标.</summary>
    public static readonly Geometry VersionRelease = Parse("M 9,1 L 16,5 L 16,13 L 9,17 L 2,13 L 2,5 Z M 2,5 L 9,9 L 16,5 M 9,9 L 9,17");

    /// <summary>Forge 铁砧图标.</summary>
    public static readonly Geometry Forge = Parse("M 2,5 L 11,1.5 L 18,5 L 9,8.5 Z M 2,5 L 2,8 L 9,11.5 L 9,8.5 M 9,11.5 L 18,8 L 18,5 M 7,10 L 7,13 L 10,14.5 L 13,13 L 13,10 M 3,13 L 10,16 L 17,13 L 14,11.8 M 6,11.8 L 3,13 L 3,16.5 L 10,19 L 17,16.5 L 17,13 M 10,16 L 10,19");

    /// <summary>NeoForge 狐狸图标.</summary>
    public static readonly Geometry NeoForge = Parse("M 1.25,8.5 H 2 L 1.8,2 Q 1.8,1.2 2.5,1.7 L 4.9,4 H 6 Q 8,3.4 10,4 H 11.1 L 13.5,1.7 Q 14.2,1.2 14.2,2 L 14,8.5 H 14.75 V 10.2 H 13.7 V 11.3 H 12.5 Q 10.7,13.3 9.4,14 Q 8,14.9 6.6,14 Q 5.3,13.3 3.5,11.3 H 2.3 V 10.2 H 1.25 Z M 3.5,3.9 L 3.6,5.3 M 12.5,3.9 L 12.4,5.3 M 4.7,7.2 V 8.8 M 11.3,7.2 V 8.8 M 3.5,10.2 H 5.1 V 10.9 H 6.4 M 12.5,10.2 H 10.9 V 10.9 H 9.6 M 7.15,10.75 H 8.85 L 8,11.75 Z");

    /// <summary>Fabric 卷轴图标.</summary>
    public static readonly Geometry Fabric = Parse("M 5,13.5 V 5 L 7,3 H 15 L 17,5 V 6.5 H 13.5 " + "M 15,3 L 13.5,5 V 14.5 L 11,17 H 5 L 3,15 V 13.5 " + "H 10 V 15 L 11,17 " + "M 7.75,7.5 H 10.75 M 7.75,10.25 H 9.25");

    /// <summary>Quilt 拼布图标.</summary>
    public static readonly Geometry Quilt = Parse("M 2.5,4 A 1.5,1.5 0 0 1 4,2.5 L 7.5,2.5 A 1.5,1.5 0 0 1 9,4 L 9,7.5 A 1.5,1.5 0 0 1 7.5,9 L 4,9 A 1.5,1.5 0 0 1 2.5,7.5 Z M 11,4 A 1.5,1.5 0 0 1 12.5,2.5 L 16,2.5 A 1.5,1.5 0 0 1 17.5,4 L 17.5,7.5 A 1.5,1.5 0 0 1 16,9 L 12.5,9 A 1.5,1.5 0 0 1 11,7.5 Z M 2.5,12.5 A 1.5,1.5 0 0 1 4,11 L 7.5,11 A 1.5,1.5 0 0 1 9,12.5 L 9,16 A 1.5,1.5 0 0 1 7.5,17.5 L 4,17.5 A 1.5,1.5 0 0 1 2.5,16 Z M 11,12.5 A 1.5,1.5 0 0 1 12.5,11 L 16,11 A 1.5,1.5 0 0 1 17.5,12.5 L 17.5,16 A 1.5,1.5 0 0 1 16,17.5 L 12.5,17.5 A 1.5,1.5 0 0 1 11,16 Z M 12.5,4 L 16,7.5 M 4,12.5 L 7.5,16");

    /// <summary>LiteLoader 蛋图标.</summary>
    public static readonly Geometry LiteLoader = Parse("M 10,1.5 C 14.5,1.5 17,8.5 17,12.3 C 17,16.2 14,18.5 10,18.5 C 6,18.5 3,16.2 3,12.3 C 3,8.5 5.5,1.5 10,1.5 Z M 6,12.5 C 6,9.8 7,7 8.6,5.4");

    /// <summary>OptiFine 罗盘图标.</summary>
    public static readonly Geometry OptiFine = Parse("M 4,10 A 6,6 0 1 1 16,10 A 6,6 0 1 1 4,10 Z M 10,1 L 10,2.8 M 10,17.2 L 10,19 M 1,10 L 2.8,10 M 17.2,10 L 19,10 M 3.6,3.6 L 4.9,4.9 M 16.4,3.6 L 15.1,4.9 M 3.6,16.4 L 4.9,15.1 M 16.4,16.4 L 15.1,15.1 M 13.5,6.5 L 11,11 L 6.5,13.5 L 9,9 Z");

    /// <summary>获取加载器图标.</summary>
    public static Geometry ForLoader(MinecraftLoaderKind kind) => kind switch
    {
        MinecraftLoaderKind.Forge => Forge,
        MinecraftLoaderKind.NeoForge => NeoForge,
        MinecraftLoaderKind.Fabric => Fabric,
        MinecraftLoaderKind.Quilt => Quilt,
        MinecraftLoaderKind.LiteLoader => LiteLoader,
        MinecraftLoaderKind.OptiFine => OptiFine,
        _ => VersionRelease
    };

    /// <summary>预览版的试验烧瓶图标.</summary>
    public static readonly Geometry VersionPreview = Parse("M 6,2 L 12,2 M 8,2 L 8,8 L 3,15 Q 2,17 5,17 L 13,17 Q 16,17 15,15 L 10,8 L 10,2 M 5,13 L 13,13");

    /// <summary>远古版的砖纹图标.</summary>
    public static readonly Geometry VersionAncient = Parse("M 2,3 L 16,3 L 16,16 L 2,16 Z M 2,9 L 16,9 M 9,3 L 9,9 M 6,9 L 6,16 M 13,9 L 13,16");

    /// <summary>愚人节版的星芒图标.</summary>
    public static readonly Geometry VersionAprilFools = Parse("M 9,1 L 11,7 L 17,9 L 11,11 L 9,17 L 7,11 L 1,9 L 7,7 Z");

    /// <summary>获取对应游戏版本类别的图标.</summary>
    public static Geometry ForVersion(MinecraftVersionKind kind) => kind switch
    {
        MinecraftVersionKind.Release => VersionRelease,
        MinecraftVersionKind.Preview => VersionPreview,
        MinecraftVersionKind.Ancient => VersionAncient,
        _ => VersionAprilFools
    };

    /// <summary>Mod 和资源图标.</summary>
    public static readonly Geometry ModsAndResources = Parse("M 7,2 L 11,2 L 11,6 L 15,6 L 15,10 L 11,10 L 11,14 L 7,14 L 7,10 L 3,10 L 3,6 L 7,6 Z");

    /// <summary>整合包图标.</summary>
    public static readonly Geometry Modpacks = Parse("M 2,5 L 9,2 L 16,5 L 9,8 Z M 2,9 L 9,12 L 16,9 M 2,13 L 9,16 L 16,13");

    /// <summary>个性化图标.</summary>
    public static readonly Geometry Personalization = Parse("M 5,2 L 8,1 L 10,4 L 12,1 L 15,2 L 18,6 L 15,8 L 14,6 L 14,17 L 6,17 L 6,6 L 5,8 L 2,6 Z");

    /// <summary>打开本地文件夹按钮图标.</summary>
    public static readonly Geometry Folder = Parse("M 2,4 L 7,4 L 9,6 L 16,6 L 16,15 L 2,15 Z");

    /// <summary>调试侧栏的终端图标.</summary>
    public static readonly Geometry Debug = Parse("M 2,3 L 16,3 L 16,15 L 2,15 Z M 5,7 L 8,9 L 5,11 M 10,12 L 13,12");

    /// <summary>百宝箱侧栏的箱子图标.</summary>
    public static readonly Geometry Toolbox = Parse("M 2,6 L 9,3 L 16,6 L 16,15 L 9,18 L 2,15 Z M 2,6 L 9,9 L 16,6 M 9,9 L 9,18 M 6,4.5 L 13,7.5");

    /// <summary>右下角任务管理入口的清单图标.</summary>
    public static readonly Geometry TaskList = Parse("M 2,4 L 4,6 L 7,2 M 10,4 L 18,4 M 2,11 L 4,13 L 7,9 M 10,11 L 18,11 M 2,18 L 4,20 L 7,16 M 10,18 L 18,18");

    private static Geometry Parse(string path)
    {
        Geometry geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
}
