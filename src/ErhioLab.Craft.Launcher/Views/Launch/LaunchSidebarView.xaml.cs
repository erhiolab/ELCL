using System.Windows;
using System.Windows.Controls;
using ErhioLab.Craft.Launcher.Dialogs;

namespace ErhioLab.Craft.Launcher.Views.Launch;

/// <summary>显示登录方式/玩家信息和游戏版本入口.</summary>
public partial class LaunchSidebarView
{
    /// <summary>初始化启动页侧栏.</summary>
    public LaunchSidebarView()
    {
        InitializeComponent();
        if (App.Settings.SelectedVersionId is { Length: > 0 } id)
        {
            ShowSelectedVersion(id);
        }
    }

    internal void ShowSelectedVersion(string id)
    {
        MainActionTitle.Text = id;
        MainActionDescription.Text = "启动游戏尚未实现";
    }

    internal void ClearSelectedVersion()
    {
        MainActionTitle.Text = "下载游戏版本";
        MainActionDescription.Text = "当前没有可启动的实例";
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
        if (App.Settings.SelectedVersionId is not null)
        {
            LauncherDialogs.Information("游戏启动", "游戏启动功能尚未实现.");
            return;
        }

        App.Logger.Information("Launch", "点击下载游戏版本入口.");
        if (Application.Current.MainWindow is MainWindow window)
        {
            window.OpenDownloadPage();
        }
    }

    private void VersionSelection_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Launch", "点击版本选择入口.");
        if (Application.Current.MainWindow is MainWindow window)
        {
            window.OpenVersionSelection();
        }
    }
}
