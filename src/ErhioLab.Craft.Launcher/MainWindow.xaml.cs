using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Views.Launch;
using ErhioLab.Craft.Launcher.Views.Shared;

namespace ErhioLab.Craft.Launcher;

public partial class MainWindow : Window
{
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmncrpDisabled = 1;
    private const int DwmwaBorderColor = 34;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private readonly IReadOnlyDictionary<ShellSection, ShellRoute> _routes;

    public MainWindow()
    {
        InitializeComponent();

        _routes = CreateRoutes();
        LaunchNavigation.IsChecked = true;
    }

    private static IReadOnlyDictionary<ShellSection, ShellRoute> CreateRoutes()
    {
        return new Dictionary<ShellSection, ShellRoute>
        {
            [ShellSection.Launch] = new(310, new LaunchSidebarView(), new LaunchPage()),
            [ShellSection.Download] = new(244,
                new SectionSidebarView("02 / DOWNLOAD", "下载", "获取游戏与扩展内容. ", "游戏版本", "Mod 与资源", "整合包"),
                new PlaceholderPage("02 / DOWNLOAD", "下载", "游戏版本/Mod/资源包与整合包会在这里提供. ")
            ),
            [ShellSection.Settings] = new(244,
                new SectionSidebarView("03 / SETTINGS", "设置", "调整启动器和游戏行为. ", "启动器", "游戏", "网络与下载"),
                new PlaceholderPage("03 / SETTINGS", "设置", "启动器外观/Java/内存和下载选项会在这里提供. ")
            ),
            [ShellSection.More] = new(220,
                new SectionSidebarView("04 / MORE", "更多", "帮助/反馈与项目信息. ", "帮助", "关于 ELCL"),
                new PlaceholderPage("04 / MORE", "更多", "帮助文档/反馈入口和关于信息会在这里提供. ")
            )
        };
    }

    private void PrimaryNavigation_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string sectionName }
            || !Enum.TryParse(sectionName, out ShellSection section)
            || !_routes.TryGetValue(section, out ShellRoute? route))
        {
            return;
        }

        SidebarColumn.Width = new GridLength(route.SidebarWidth);
        SidebarHost.Content = route.Sidebar;
        PageHost.Content = route.Page;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int renderingPolicy = DwmncrpDisabled;
        int borderColor = DwmColorNone;

        _ = DwmSetWindowAttribute(handle, DwmwaNcRenderingPolicy, ref renderingPolicy, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
