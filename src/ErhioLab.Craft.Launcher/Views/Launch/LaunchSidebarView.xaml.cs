using System.Windows;
using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Views.Launch;

/// <summary>显示登录方式/玩家信息和游戏版本入口.</summary>
public partial class LaunchSidebarView : UserControl
{
    /// <summary>初始化启动页侧栏.</summary>
    public LaunchSidebarView()
    {
        InitializeComponent();
    }

    private void LoginMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button)
        {
            App.Logger.Information("Login", $"选择登录方式: {button.Content}");
        }
    }

    private void DownloadVersion_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Launch", "点击下载游戏版本入口.");
    }

    private void VersionSelection_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Launch", "点击版本选择入口.");
    }
}
