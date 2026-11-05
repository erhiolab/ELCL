using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Views.Download;
using ErhioLab.Craft.Launcher.Views.Launch;
using ErhioLab.Craft.Launcher.Views.More;
using ErhioLab.Craft.Launcher.Views.Settings;
using ErhioLab.Craft.Launcher.Views.Shared;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>集中定义顶部导航对应的侧栏和主体视图.</summary>
internal static class ShellRouteCatalog
{
    /// <summary>为主窗口创建本次使用的页面路由.</summary>
    /// <returns>按顶部导航项索引的页面路由.</returns>
    internal static IReadOnlyDictionary<ShellSection, ShellRoute> Create()
    {
        // 下载页当前只提供原版游戏版本列表.
        SectionSidebarItem vanillaItem = new("sidebar.vanilla", LauncherIcons.GameVersions, CanRefresh: true);
        SectionSidebarView downloadSidebar = new(vanillaItem);
        VanillaVersionsPage versionList = new();
        AnimatedPageHost downloadContent = new(versionList);
        versionList.VersionSelected += version =>
        {
            VanillaVersionDetailsPage details = new(version);
            details.BackRequested += () => downloadContent.Navigate(versionList, back: true);
            downloadContent.Navigate(details);
        };
        downloadSidebar.ActionRequested += _ => versionList.RefreshVersions();

        // 设置
        LaunchSettingsPage launchSettingsPage = new();
        SectionSidebarItem launchSettingsItem = new("sidebar.launch", LauncherIcons.LaunchSettings, CanReset: true);
        PersonalizationPage personalizationPage = new();
        SectionSidebarItem personalizationItem = new("sidebar.personalization", LauncherIcons.Personalization, CanReset: true);
        DownloadSettingsPage downloadSettingsPage = new();
        SectionSidebarItem downloadSettingsItem = new("sidebar.download", LauncherIcons.DownloadSettings, CanReset: true);
        OtherSettingsPage otherSettingsPage = new();
        SectionSidebarItem otherItem = new("sidebar.other", LauncherIcons.More);
        DebugSettingsPage debugSettingsPage = new();
        SectionSidebarItem debugItem = new("sidebar.debug", LauncherIcons.Debug);
        SectionSidebarView settingsSidebar = new(
            launchSettingsItem,
            personalizationItem,
            downloadSettingsItem,
            otherItem,
            debugItem
        );
        ContentControl settingsContent = new()
        {
            Content = launchSettingsPage,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        settingsSidebar.SelectionChanged += item =>
        {
            if (ReferenceEquals(item, launchSettingsItem))
            {
                settingsContent.Content = launchSettingsPage;
            }
            else if (ReferenceEquals(item, downloadSettingsItem))
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
            if (ReferenceEquals(item, launchSettingsItem) && await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("reset.launchTitle"), LocalizationManager.Get("reset.launchPrompt"), confirmText: LocalizationManager.Get("reset.button")))
            {
                await App.JavaCatalog.ResetAsync();
            }
            else if (ReferenceEquals(item, personalizationItem) && await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("reset.personalizationTitle"), LocalizationManager.Get("reset.personalizationPrompt"), confirmText: LocalizationManager.Get("reset.button")))
            {
                personalizationPage.ResetToDefaults();
            }
            else if (ReferenceEquals(item, downloadSettingsItem) && await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("reset.downloadTitle"), LocalizationManager.Get("reset.downloadPrompt"), confirmText: LocalizationManager.Get("reset.button")))
            {
                downloadSettingsPage.ResetToDefaults();
            }
        };

        // 更多
        SectionSidebarItem aboutItem = new("sidebar.about", LauncherIcons.About);
        SectionSidebarItem toolboxItem = new("sidebar.toolbox", LauncherIcons.Toolbox);
        SectionSidebarView moreSidebar = new(aboutItem, toolboxItem);
        AboutPage aboutPage = new();
        ContentControl moreContent = new()
        {
            Content = aboutPage,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        ToolboxPage toolboxPage = new();
        moreSidebar.SelectionChanged += item => moreContent.Content = ReferenceEquals(item, toolboxItem) ? toolboxPage : aboutPage;

        return new Dictionary<ShellSection, ShellRoute>
        {
            [ShellSection.Launch] = new(310, new LaunchSidebarView(), new LaunchPage()),
            [ShellSection.Download] = new(0, downloadSidebar, downloadContent),
            [ShellSection.Settings] = new(0, settingsSidebar, settingsContent),
            [ShellSection.More] = new(0, moreSidebar, moreContent)
        };
    }
}
