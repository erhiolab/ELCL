using System.Windows;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>定义一级页面使用的侧栏宽度和两个视图.</summary>
/// <param name="SidebarWidth">侧栏宽度, 单位为 WPF 设备无关像素. 0 表示由内容自动决定.</param>
/// <param name="Sidebar">该页面的侧栏视图.</param>
/// <param name="Page">该页面的主体视图.</param>
internal sealed record ShellRoute(
    double SidebarWidth,
    FrameworkElement Sidebar,
    FrameworkElement Page
);
