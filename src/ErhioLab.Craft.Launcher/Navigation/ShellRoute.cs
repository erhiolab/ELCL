using System.Windows;

namespace ErhioLab.Craft.Launcher.Navigation;

internal sealed record ShellRoute(
    double SidebarWidth,
    FrameworkElement Sidebar,
    FrameworkElement Page
);