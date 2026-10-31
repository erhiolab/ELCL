using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ErhioLab.Craft.Launcher.Core;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Views.More;

/// <summary>显示启动器/核心版本与所用项目和服务的鸣谢.</summary>
public partial class AboutPage
{
    /// <summary>初始化关于与鸣谢页面.</summary>
    public AboutPage()
    {
        InitializeComponent();

        string launcherVersion = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";

        AboutEntries.ItemsSource = new[]
        {
            new CreditEntry("EL", "ErhioLab Craft Launcher", $"启动器版本: {launcherVersion}"),
            new CreditEntry("CO", "ErhioLab Craft Launcher Core", $"核心版本: {CoreInfo.Version}")
        };
        ThanksEntries.ItemsSource = new[]
        {
            new CreditEntry("P", "PCL2", "界面布局与交互设计的参考.", "项目主页", "https://github.com/Meloong-Git/PCL/", LoadAvatar("Pcl.ico")),
            new CreditEntry("BM", "BMCLAPI", "提供 Minecraft 版本清单的备用镜像源.", "赞助镜像源", "https://afdian.com/a/bangbang93", LoadAvatar("Bmclapi.jpg")),
            new CreditEntry("MC", "Minecraft Wiki", "提供游戏版本的更新摘要和更新日志页面.", "打开 Wiki", "https://zh.minecraft.wiki/"),
            new CreditEntry("UI", "WPF UI", "提供界面控件与主题资源.")
        };
    }

    private static ImageSource LoadAvatar(string fileName) => new BitmapImage(new Uri($"pack://application:,,,/ErhioLab.Craft.Launcher;component/Assets/Credits/{fileName}", UriKind.Absolute));

    private void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            App.Logger.Information("More", $"打开鸣谢链接: {url}");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("More", $"无法打开鸣谢链接: {url}", error);
            LauncherToasts.Error("无法在浏览器中打开页面.");
        }
    }

    private sealed record CreditEntry(
        string AvatarText,
        string Name,
        string Description,
        string? ActionText = null,
        string? Url = null,
        ImageSource? AvatarImage = null);
}
