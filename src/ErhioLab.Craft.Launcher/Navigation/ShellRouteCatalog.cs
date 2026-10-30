using System.Windows;
using System.Windows.Controls;

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
        PersonalizationPage personalizationPage = new();
        OtherSettingsPage otherSettingsPage = new();
        SectionSidebarItem personalizationItem = new("个性化", LauncherIcons.Personalization, CanReset: true);
        SectionSidebarItem otherItem = new("其他", LauncherIcons.More);
        SectionSidebarView settingsSidebar = new(
            personalizationItem,
            otherItem);
        ContentControl settingsContent = new()
        {
            Content = personalizationPage,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        settingsSidebar.SelectionChanged += item =>
            settingsContent.Content = ReferenceEquals(item, otherItem) ? otherSettingsPage : personalizationPage;
        settingsSidebar.ResetRequested += async _ =>
        {
            if (await LauncherDialogs.ConfirmAsync(
                    "重置个性化设置",
                    "是否要重置 个性化 的所有设置? 此操作不可逆.",
                    LauncherDialogLevel.Danger,
                    confirmText: "重置"))
            {
                personalizationPage.ResetToDefaults();
            }
        };

        return new Dictionary<ShellSection, ShellRoute>
        {
            [ShellSection.Launch] = new(310, new LaunchSidebarView(), new LaunchPage()),
            [ShellSection.Download] = new(
                0,
                new SectionSidebarView(
                    new SectionSidebarItem("游戏版本", LauncherIcons.GameVersions),
                    new SectionSidebarItem("Mod 与资源", LauncherIcons.ModsAndResources),
                    new SectionSidebarItem("整合包", LauncherIcons.Modpacks)),
                new PlaceholderPage(
                    "02 / DOWNLOAD", "下载", "游戏版本/Mod/资源包与整合包会在这里提供. ")),
            [ShellSection.Settings] = new(0, settingsSidebar, settingsContent),
            [ShellSection.More] = new(
                0,
                new SectionSidebarView(
                    new SectionSidebarItem("帮助", LauncherIcons.Help),
                    new SectionSidebarItem("关于 ELCL", LauncherIcons.About)),
                new PlaceholderPage(
                    "04 / MORE", "更多", "帮助文档/反馈入口和关于信息会在这里提供. "))
        };
    }
}
