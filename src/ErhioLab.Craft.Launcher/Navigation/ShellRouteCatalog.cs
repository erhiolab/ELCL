using ErhioLab.Craft.Launcher.Views.Launch;
using ErhioLab.Craft.Launcher.Views.Settings;
using ErhioLab.Craft.Launcher.Views.Shared;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>集中定义顶部导航对应的侧栏和主体视图.</summary>
internal static class ShellRouteCatalog
{
    /// <summary>为主窗口创建本次使用的页面路由.</summary>
    /// <returns>按顶部导航项索引的页面路由.</returns>
    internal static IReadOnlyDictionary<ShellSection, ShellRoute> Create() =>
        new Dictionary<ShellSection, ShellRoute>
        {
            [ShellSection.Launch] = new(310, new LaunchSidebarView(), new LaunchPage()),
            [ShellSection.Download] = new(
                0,
                new SectionSidebarView(
                    new SectionSidebarItem("游戏版本", SectionSidebarIcons.GameVersions),
                    new SectionSidebarItem("Mod 与资源", SectionSidebarIcons.ModsAndResources),
                    new SectionSidebarItem("整合包", SectionSidebarIcons.Modpacks)),
                new PlaceholderPage(
                    "02 / DOWNLOAD", "下载", "游戏版本/Mod/资源包与整合包会在这里提供. ")),
            [ShellSection.Settings] = new(
                0,
                new SectionSidebarView(
                    new SectionSidebarItem("个性化", SectionSidebarIcons.Personalization)),
                new PersonalizationPage()),
            [ShellSection.More] = new(
                0,
                new SectionSidebarView(
                    new SectionSidebarItem("帮助", SectionSidebarIcons.Help),
                    new SectionSidebarItem("关于 ELCL", SectionSidebarIcons.About)),
                new PlaceholderPage(
                    "04 / MORE", "更多", "帮助文档/反馈入口和关于信息会在这里提供. "))
        };
}
