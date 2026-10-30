using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Views.Download;
using ErhioLab.Craft.Launcher.Views.Launch;
using ErhioLab.Craft.Launcher.Views.Settings;
using ErhioLab.Craft.Launcher.Views.Shared;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Icons;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>集中定义顶部导航对应的侧栏和主体视图.</summary>
internal static class ShellRouteCatalog
{
    /// <summary>为主窗口创建本次使用的页面路由.</summary>
    /// <returns>按顶部导航项索引的页面路由.</returns>
    internal static IReadOnlyDictionary<ShellSection, ShellRoute> Create()
    {
        // 下载页当前只提供原版游戏版本列表.
        SectionSidebarItem vanillaItem = new("原版游戏", LauncherIcons.GameVersions, CanRefresh: true);
        SectionSidebarView downloadSidebar = new(vanillaItem);
        VanillaVersionsPage downloadContent = new();
        downloadSidebar.ActionRequested += _ => downloadContent.RefreshVersions();

        // 设置
        PersonalizationPage personalizationPage = new();
        SectionSidebarItem personalizationItem = new("个性化", LauncherIcons.Personalization, CanReset: true);
        DownloadSettingsPage downloadSettingsPage = new();
        SectionSidebarItem downloadSettingsItem = new("下载", LauncherIcons.DownloadSettings, CanReset: true);
        OtherSettingsPage otherSettingsPage = new();
        SectionSidebarItem otherItem = new("其他", LauncherIcons.More);
        DebugSettingsPage debugSettingsPage = new();
        SectionSidebarItem debugItem = new("调试", LauncherIcons.Debug);
        SectionSidebarView settingsSidebar = new(
            personalizationItem,
            downloadSettingsItem,
            otherItem,
            debugItem
        );
        ContentControl settingsContent = new()
        {
            Content = personalizationPage,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        settingsSidebar.SelectionChanged += item =>
        {
            if (ReferenceEquals(item, downloadSettingsItem))
            {
                settingsContent.Content = downloadSettingsPage;
            }
            else if (ReferenceEquals(item, otherItem))
            {
                settingsContent.Content = otherSettingsPage;
            }
            else if (ReferenceEquals(item, debugItem))
            {
                settingsContent.Content = debugSettingsPage;
            }
            else
            {
                settingsContent.Content = personalizationPage;
            }
        };
        settingsSidebar.ActionRequested += async item =>
        {
            if (ReferenceEquals(item, personalizationItem) && await LauncherDialogs.ConfirmAsync("重置个性化设置", "是否要重置 个性化 的所有设置? 此操作不可逆.", confirmText: "重置"))
            {
                personalizationPage.ResetToDefaults();
            }
            else if (ReferenceEquals(item, downloadSettingsItem) && await LauncherDialogs.ConfirmAsync("重置下载设置", "是否要重置 下载 的所有设置? 此操作不可逆.", confirmText: "重置"))
            {
                downloadSettingsPage.ResetToDefaults();
            }
        };

        return new Dictionary<ShellSection, ShellRoute>
        {
            [ShellSection.Launch] = new(310, new LaunchSidebarView(), new LaunchPage()),
            [ShellSection.Download] = new(0, downloadSidebar, downloadContent),
            [ShellSection.Settings] = new(0, settingsSidebar, settingsContent),
            [ShellSection.More] = new(
                0,
                new SectionSidebarView(new SectionSidebarItem("帮助", LauncherIcons.Help), new SectionSidebarItem("关于 ELCL", LauncherIcons.About)),
                new PlaceholderPage("04 / MORE", "更多", "帮助文档/反馈入口和关于信息会在这里提供. "))
        };
    }
}
