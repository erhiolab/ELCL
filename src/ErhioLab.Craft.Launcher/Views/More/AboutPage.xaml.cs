using ErhioLab.Craft.Launcher.Localization;
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
        LocalizationManager.Changed += (_, _) => RefreshCredits();
        RefreshCredits();
    }

    private void RefreshCredits()
    {
        string launcherVersion = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
        ImageSource brandIcon = LoadBrandIcon();

        AboutEntries.ItemsSource = new[]
        {
            new CreditEntry("EL", LocalizationManager.Get("about.erhai"), LocalizationManager.Get("about.elclDeveloperDescription"), LocalizationManager.Get("about.githubRepository"), "https://github.com/ErhioLab/ELCL"),
            new CreditEntry("EL", "ErhioLab Craft Launcher", string.Format(LocalizationManager.Get("about.launcherVersion"), launcherVersion), AvatarImage: brandIcon),
            new CreditEntry("CO", "ErhioLab Craft Launcher Core", string.Format(LocalizationManager.Get("about.coreVersion"), CoreInfo.Version), AvatarImage: brandIcon)
        };
        ThanksEntries.ItemsSource = new[]
        {
            new CreditEntry("P", "PCL2", LocalizationManager.Get("about.layoutAndCodeReference"), LocalizationManager.Get("about.projectHomepage"), "https://github.com/Meloong-Git/PCL/", LoadAvatar("Pcl.png")),
            new CreditEntry("PL", "Prism Launcher", LocalizationManager.Get("about.codeReference"), LocalizationManager.Get("about.projectHomepage"), "https://github.com/PrismLauncher/PrismLauncher", LoadAvatar("PrismLauncher.png")),
            new CreditEntry("BM", "BMCLAPI", LocalizationManager.Get("about.mirrorDescription"), LocalizationManager.Get("about.sponsorMirror"), "https://afdian.com/a/bangbang93", LoadAvatar("Bmclapi.jpg")),
            new CreditEntry("MC", "Minecraft Wiki", LocalizationManager.Get("about.wikiDescription"), LocalizationManager.Get("about.openWiki"), "https://zh.minecraft.wiki/", LoadAvatar("MinecraftWiki.png"))
        };
    }

    private static ImageSource LoadAvatar(string fileName) => LoadImage($"Assets/Credits/{fileName}");

    private static ImageSource LoadBrandIcon() => LoadImage("Assets/Brand/ELCL.png");

    private static ImageSource LoadImage(string path) => new BitmapImage(new Uri($"pack://application:,,,/ErhioLab.Craft.Launcher;component/{path}", UriKind.Absolute));

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
            LauncherToasts.Error(LocalizationManager.Get("about.openBrowserFailed"));
        }
    }

    internal sealed record CreditEntry(
        string AvatarText,
        string Name,
        string Description,
        string? ActionText = null,
        string? Url = null,
        ImageSource? AvatarImage = null);
}
