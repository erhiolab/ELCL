using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Views.Download;
using ErhioLab.Craft.Launcher.Views.Launch;
using ErhioLab.Craft.Launcher.Views.More;
using ErhioLab.Craft.Launcher.Views.Settings;
using ErhioLab.Craft.Launcher.Views.Shared;
using ErhioLab.Craft.Launcher.Views.Tasks;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>集中定义顶部导航对应的侧栏和主体视图.</summary>
internal static class ShellRouteCatalog
{
    /// <summary>为主窗口创建本次使用的页面路由.</summary>
    /// <returns>按顶部导航项索引的页面路由.</returns>
    internal static IReadOnlyDictionary<ShellSection, ShellRoute> Create()
    {
        // 游戏与 Java 下载共用侧栏和缓动页面容器.
        SectionSidebarItem vanillaItem = new("sidebar.vanilla", LauncherIcons.VersionRelease, CanRefresh: true, ActionToolTipKey: "sidebar.refreshVanilla");
        SectionSidebarItem javaItem = new("javaDownload.title", LauncherIcons.Java, CanRefresh: true, ActionToolTipKey: "sidebar.refreshJava");
        SectionSidebarItem modsItem = new("mods.title", LauncherIcons.ModsAndResources, CanRefresh: true, ActionToolTipKey: "sidebar.refreshMods");
        SectionSidebarView downloadSidebar = new(vanillaItem, modsItem, javaItem);
        VanillaVersionsPage versionList = new();
        JavaDownloadPage javaDownloads = new();
        ModDownloadPage modDownloads = new();
        AnimatedPageHost downloadContent = new(versionList);
        versionList.VersionSelected += version =>
        {
            VanillaVersionDetailsPage details = new(version);
            details.BackRequested += () => downloadContent.Navigate(versionList, back: true);
            downloadContent.Navigate(details);
        };
        void OpenModDetails(Mods.ModProjectRow row, Mods.ModBrowserContext context, FrameworkElement parent)
        {
            ModDetailsPage details = new(row, context);
            details.BackRequested += () => downloadContent.Navigate(parent, back: true);
            details.ProjectRequested += (dependency, selectedContext) => OpenModDetails(dependency, selectedContext, details);
            downloadContent.Navigate(details);
        }
        modDownloads.ProjectSelected += (row, context) => OpenModDetails(row, context, modDownloads);
        downloadSidebar.SelectionChanged += item => downloadContent.Navigate(ReferenceEquals(item, javaItem) ? javaDownloads
            : ReferenceEquals(item, modsItem) ? modDownloads : versionList, back: ReferenceEquals(item, vanillaItem));
        downloadSidebar.ActionRequested += item =>
        {
            if (ReferenceEquals(item, javaItem)) javaDownloads.RefreshCatalogs();
            else if (ReferenceEquals(item, modsItem)) modDownloads.Refresh();
            else versionList.RefreshVersions();
        };

        // 设置
        LaunchSettingsPage launchSettingsPage = new();
        SectionSidebarItem launchSettingsItem = new("sidebar.launch", LauncherIcons.Launch, CanReset: true, HiddenFeature: LauncherHiddenFeatures.LaunchSettings);
        PersonalizationPage personalizationPage = new();
        SectionSidebarItem personalizationItem = new("sidebar.personalization", LauncherIcons.Personalization, CanReset: true, HiddenFeature: LauncherHiddenFeatures.Personalization);
        var backgroundPage = new LauncherBackgroundPage();
        var personalizationHost = new AnimatedPageHost(personalizationPage, resetOnUnload: true);
        personalizationPage.BackgroundManagementRequested += () =>
            personalizationHost.Navigate(backgroundPage, onShown: backgroundPage.QueueGalleryLoad);
        backgroundPage.BackRequested += () => personalizationHost.Navigate(personalizationPage, back: true);
        DownloadSettingsPage downloadSettingsPage = new();
        SectionSidebarItem downloadSettingsItem = new("sidebar.download", LauncherIcons.Download, CanReset: true, HiddenFeature: LauncherHiddenFeatures.DownloadSettings);
        OtherSettingsPage otherSettingsPage = new();
        SectionSidebarItem otherItem = new("sidebar.other", LauncherIcons.More, HiddenFeature: LauncherHiddenFeatures.OtherSettings);
        DebugSettingsPage debugSettingsPage = new();
        var debugHost = new AnimatedPageHost(debugSettingsPage, resetOnUnload: true);
        GameLogView? launcherLogView = null;
        debugSettingsPage.LauncherLogRequested += (path, isCurrent) =>
        {
            if (launcherLogView is null)
            {
                launcherLogView = new GameLogView();
                launcherLogView.BackRequested += () => debugHost.Navigate(debugSettingsPage, back: true);
                launcherLogView.Unloaded += (_, _) => launcherLogView.CloseFile();
            }

            launcherLogView.ShowLauncherFile(path, isCurrent);
            debugHost.Navigate(launcherLogView);
        };
        SectionSidebarItem debugItem = new("sidebar.debug", LauncherIcons.Debug, HiddenFeature: LauncherHiddenFeatures.Debug);
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
                settingsContent.Content = debugHost;
            }
            else
            {
                settingsContent.Content = personalizationHost;
            }
        };
        settingsSidebar.ActionRequested += async item =>
        {
            if (ReferenceEquals(item, launchSettingsItem) && await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("reset.launchTitle"), LocalizationManager.Get("reset.launchPrompt"), confirmText: LocalizationManager.Get("reset.button")))
            {
                launchSettingsPage.ResetToDefaults();
            }
            else if (ReferenceEquals(item, personalizationItem) && await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("reset.personalizationTitle"), LocalizationManager.Get("reset.personalizationPrompt"), confirmText: LocalizationManager.Get("reset.button")))
            {
                personalizationPage.ResetToDefaults();
                backgroundPage.ReloadOptions();
            }
            else if (ReferenceEquals(item, downloadSettingsItem) && await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("reset.downloadTitle"), LocalizationManager.Get("reset.downloadPrompt"), confirmText: LocalizationManager.Get("reset.button")))
            {
                downloadSettingsPage.ResetToDefaults();
            }
        };

        // 更多
        SectionSidebarItem aboutItem = new("sidebar.about", LauncherIcons.Info, HiddenFeature: LauncherHiddenFeatures.About);
        SectionSidebarItem toolboxItem = new("sidebar.toolbox", LauncherIcons.Toolbox, HiddenFeature: LauncherHiddenFeatures.Toolbox);
        SectionSidebarItem performanceItem = new("sidebar.performance", LauncherIcons.Performance, HiddenFeature: LauncherHiddenFeatures.Performance);
        SectionSidebarView moreSidebar = new(aboutItem, toolboxItem, performanceItem);
        AboutPage aboutPage = new();
        ContentControl moreContent = new()
        {
            Content = aboutPage,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        ToolboxPage toolboxPage = new();
        PerformancePage performancePage = new();
        moreSidebar.SelectionChanged += item => moreContent.Content = ReferenceEquals(item, toolboxItem) ? toolboxPage
            : ReferenceEquals(item, performanceItem) ? performancePage : aboutPage;

        return new Dictionary<ShellSection, ShellRoute>
        {
            [ShellSection.Launch] = new(310, new LaunchSidebarView(), new LaunchPage()),
            [ShellSection.Download] = new(0, downloadSidebar, downloadContent),
            [ShellSection.Settings] = new(0, settingsSidebar, settingsContent),
            [ShellSection.More] = new(0, moreSidebar, moreContent)
        };
    }
}
