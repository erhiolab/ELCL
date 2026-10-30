using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>为侧栏模板提供可识别的导航项数据.</summary>
internal sealed record SectionSidebarEntry(SectionSidebarItem Item, bool IsFirst)
{
    /// <summary>侧栏显示的名称.</summary>
    public string Title => Item.Title;

    /// <summary>侧栏显示的图标.</summary>
    public Geometry Icon => Item.Icon;

    /// <summary>是否显示悬浮重置按钮.</summary>
    public bool CanReset => Item.CanReset;
}
