using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>侧栏条目的显示名称和矢量图标.</summary>
/// <param name="Title">侧栏显示的名称.</param>
/// <param name="Icon">侧栏显示的矢量图标.</param>
/// <param name="CanReset">是否显示悬浮重置按钮.</param>
/// <param name="CanRefresh">是否显示悬浮刷新按钮.</param>
internal sealed record SectionSidebarItem(string Title, Geometry Icon, bool CanReset = false, bool CanRefresh = false);
