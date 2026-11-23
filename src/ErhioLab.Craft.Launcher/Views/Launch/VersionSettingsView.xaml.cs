using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Views.Download;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Versions;
using ErhioLab.Craft.Launcher.Views.Shared;
using ErhioLab.Craft.Launcher.Views.Settings;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Launch;

public partial class VersionSettingsView
{
    internal FrameworkElement Sidebar => SidebarHost;
    internal LocalMinecraftVersion? Version { get; private set; }
    private string _gameDirectory = "";
    private int _generation;
    private readonly LaunchSettingsPage _launchSettingsPage;
    private readonly VersionLogsPage _logsPage;
    private readonly VersionModsPage _modsPage;
    private readonly VersionPacksPage _resourcePacksPage;
    private readonly VersionPacksPage _shaderPacksPage;
    private readonly VersionWorldsPage _worldsPage;
    private readonly VersionPacksPage _dataPacksPage;
    private readonly VersionExportPage _exportPage;
    private readonly AnimatedPageHost _pageHost;
    private readonly Stack<FrameworkElement> _modHistory = new();
    internal event Action? BackRequested;

    public VersionSettingsView()
    {
        InitializeComponent();
        ViewLayout.Children.Remove(SidebarHost);
        _launchSettingsPage = new LaunchSettingsPage();
        _logsPage = new VersionLogsPage();
        PageHost.Content = null;
        _pageHost = new AnimatedPageHost(OverviewPage);
        PageHost.Content = _pageHost;
        _modsPage = new VersionModsPage();
        _resourcePacksPage = new VersionPacksPage(MinecraftPackKind.ResourcePack);
        _shaderPacksPage = new VersionPacksPage(MinecraftPackKind.ShaderPack);
        _worldsPage = new VersionWorldsPage();
        _dataPacksPage = new VersionPacksPage(MinecraftPackKind.DataPack);
        _exportPage = new VersionExportPage();
        _modsPage.BrowseRequested += context => OpenCommunityBrowser(ModProjectKind.Mod, context, _modsPage);
        _modsPage.DetailsRequested += (row, context) => OpenModDetails(row, context, _modsPage);
        ConnectPacksPage(_resourcePacksPage);
        ConnectPacksPage(_shaderPacksPage);
        ConnectPacksPage(_dataPacksPage);
        _worldsPage.DataPacksRequested += world =>
        {
            if (Version is not { } version) return;
            _dataPacksPage.SetWorldContext(_gameDirectory, version, world);
            _modHistory.Push(_worldsPage);
            _pageHost.Navigate(_dataPacksPage);
        };
        _worldsPage.QuickPlayRequested += world => Version is { } version
            ? App.GameLauncher.StartWorldAsync(_gameDirectory, version, world) : Task.CompletedTask;
        _dataPacksPage.BackRequested += () => CloseModPage();
        Unloaded += (_, _) => _launchSettingsPage.FlushPendingChanges();
        LocalizationManager.Changed += (_, _) => RefreshSummary();
    }

    private void ConnectPacksPage(VersionPacksPage page)
    {
        page.BrowseRequested += (kind, context) => OpenCommunityBrowser(kind, context, page);
        page.DetailsRequested += (row, context) => OpenModDetails(row, context, page);
    }

    private void OpenCommunityBrowser(ModProjectKind kind, ModBrowserContext context, FrameworkElement parent)
    {
        var browser = new ModDownloadPage(kind);
        browser.SetContext(context);
        browser.ProjectSelected += (row, selectedContext) => OpenModDetails(row, selectedContext, browser);
        _modHistory.Push(parent);
        _pageHost.Navigate(browser);
    }

    private void OpenModDetails(ModProjectRow row, ModBrowserContext context, FrameworkElement parent)
    {
        var details = new ModDetailsPage(row, context);
        details.BackRequested += () => CloseModPage();
        details.ProjectRequested += (project, selectedContext) => OpenModDetails(project, selectedContext, details);
        _modHistory.Push(parent);
        _pageHost.Navigate(details);
    }

    private void RefreshSidebar()
    {
        List<(SectionSidebarItem Item, FrameworkElement Page, Func<Task>? Action)> routes = [];
        void Add(string key, System.Windows.Media.Geometry icon, FrameworkElement page, Func<Task>? refresh = null, string? tooltip = null) =>
            routes.Add((new SectionSidebarItem(key, icon, CanRefresh: refresh is not null,
                ActionToolTipKey: refresh is null ? null : tooltip ?? "versionSettings.refreshList"), page, refresh));

        Add("versionSettings.overview", LauncherIcons.VersionRelease, OverviewPage);
        routes.Add((new SectionSidebarItem("versionSettings.settings", LauncherIcons.Settings, CanReset: true,
            ActionToolTipKey: "versionLaunch.reset"), _launchSettingsPage, ConfirmVersionResetAsync));
        if (Version is { } current && CommunityMods.HasLoader(current))
            Add("mods.manage", LauncherIcons.ModsAndResources, _modsPage, _modsPage.RefreshAsync);
        Add("community.resourcePacks", LauncherIcons.Image, _resourcePacksPage, _resourcePacksPage.RefreshAsync);
        Add("community.shaderPacks", LauncherIcons.DayTheme, _shaderPacksPage, _shaderPacksPage.RefreshAsync);
        Add("worlds.title", LauncherIcons.Globe, _worldsPage, () => ReferenceEquals(_pageHost.CurrentPage, _dataPacksPage)
            ? _dataPacksPage.RefreshAsync() : _worldsPage.RefreshAsync());
        Add("versionLogs.title", LauncherIcons.Document, _logsPage, () => _logsPage.RefreshAsync(notify: true));
        Add("packExport.title", LauncherIcons.Toolbox, _exportPage, _exportPage.RefreshAsync, "packExport.rescan");

        var byItem = routes.ToDictionary(route => route.Item);
        var sidebar = new SectionSidebarView(routes.Select(route => route.Item).ToArray());
        SidebarHost.Content = sidebar;
        sidebar.SelectionChanged += item =>
        {
            _launchSettingsPage.FlushPendingChanges();
            _modHistory.Clear();
            var route = byItem[item];
            if (ReferenceEquals(route.Page, _launchSettingsPage) && Version is { } version)
                _launchSettingsPage.SetVersionContext(_gameDirectory, version);
            _pageHost.Navigate(route.Page, back: ReferenceEquals(route.Page, OverviewPage));
        };
        sidebar.ActionRequested += async item =>
        {
            if (byItem[item].Action is { } action) await action();
        };
        _pageHost.Navigate(OverviewPage, back: true);
    }

    private async Task ConfirmVersionResetAsync()
    {
        if (Version is not { } version) return;
        if (await LauncherDialogs.ConfirmAsync(L("versionLaunch.reset"), L("versionLaunch.resetPrompt"), confirmText: L("reset.button"))
            && ReferenceEquals(Version, version))
            _launchSettingsPage.ResetVersionOptions();
    }

    internal void SetVersion(string gameDirectory, LocalMinecraftVersion version)
    {
        _launchSettingsPage.SetVersionContext(gameDirectory, version);
        _gameDirectory = Path.GetFullPath(gameDirectory);
        Version = version;
        _exportPage.SetContext(_gameDirectory, version);
        _modsPage.SetContext(new ModBrowserContext(version.GameVersion, CommunityMods.Loader(version.Loader), version.FolderPath));
        _resourcePacksPage.SetContext(_gameDirectory, version);
        _shaderPacksPage.SetContext(_gameDirectory, version);
        _worldsPage.SetVersion(version);
        _modHistory.Clear();
        RefreshSidebar();
        _logsPage.SetVersionDirectory(version.FolderPath);
        _generation++;
        ActionsPanel.IsEnabled = true;
        SidebarHost.IsEnabled = true;
        RenameReveal.IsExpanded = false;
        DescriptionReveal.IsExpanded = false;
        RefreshSummary();
    }

    internal bool TryCloseLog()
    {
        return CloseModPage() || _logsPage.CloseLog();
    }

    private bool CloseModPage()
    {
        if (!_modHistory.TryPop(out var parent)) return false;
        _pageHost.Navigate(parent, back: true);
        return true;
    }

    private void RefreshSummary()
    {
        if (Version is not { } version) return;
        VersionSummary.Info = new LocalVersionPresentation(version, VersionSettingsStore.ReadDescription(version.FolderPath));
        ModsButton.Visibility = Directory.Exists(Path.Combine(version.FolderPath, "mods")) || version.Loader is "Forge" or "NeoForge" or "Fabric" or "Quilt" or "LiteLoader"
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExportButton.IsEnabled = RepairButton.IsEnabled = version.IsValid;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Version is null) return;
        DescriptionReveal.IsExpanded = false;
        NameEditor.Text = Path.GetFileName(Version.FolderPath);
        RenameReveal.IsExpanded = !RenameReveal.IsExpanded;
        if (RenameReveal.IsExpanded)
        {
            NameEditor.Focus();
            NameEditor.SelectAll();
        }
    }

    private void CancelRename_Click(object sender, RoutedEventArgs e) => RenameReveal.IsExpanded = false;

    private void Description_Click(object sender, RoutedEventArgs e)
    {
        if (Version is not { } version) return;
        RenameReveal.IsExpanded = false;
        DescriptionEditor.Text = VersionSettingsStore.ReadDescription(version.FolderPath);
        DescriptionReveal.IsExpanded = !DescriptionReveal.IsExpanded;
        if (DescriptionReveal.IsExpanded)
        {
            DescriptionEditor.Focus();
            DescriptionEditor.SelectAll();
        }
    }

    private void CancelDescription_Click(object sender, RoutedEventArgs e) => DescriptionReveal.IsExpanded = false;
    private async void SaveDescription_Click(object sender, RoutedEventArgs e) => await SaveDescriptionAsync();

    private async void DescriptionEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DescriptionReveal.IsExpanded = false;
            e.Handled = true;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SaveDescriptionAsync();
        }
    }

    private async Task SaveDescriptionAsync()
    {
        if (Version is not { } version || !ActionsPanel.IsEnabled) return;
        string description = DescriptionEditor.Text;
        await OperateAsync(async () =>
        {
            await VersionManagementService.SaveDescriptionAsync(version, description);
            LauncherToasts.Success(L("versionSettings.descriptionSaved"));
        });
    }

    private async void ConfirmRename_Click(object sender, RoutedEventArgs e) => await RenameAsync();

    private async void NameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RenameReveal.IsExpanded = false;
            e.Handled = true;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RenameAsync();
        }
    }

    private async Task RenameAsync()
    {
        if (Version is not { } version || !ActionsPanel.IsEnabled) return;
        string name = NameEditor.Text.Trim();
        string gameDirectory = _gameDirectory;
        int generation = _generation;
        await OperateAsync(async () =>
        {
            var renamed = await VersionManagementService.RenameAsync(gameDirectory, version, name);
            if (renamed is not null && generation == _generation) SetVersion(gameDirectory, renamed);
        });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Version is not { } version || sender is not Button { Tag: string relative }) return;
        try
        {
            if (!Directory.Exists(version.FolderPath)) throw new DirectoryNotFoundException(L("versionSelector.directoryChanged"));
            if (relative is not ("" or "saves" or "mods" or "screenshots" or "shaderpacks" or "resourcepacks" or "logs")) return;
            string path = Path.Combine(version.FolderPath, relative);
            App.Logger.Information("Versions", $"开始打开版本目录: {path}.");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            App.Logger.Information("Versions", $"已打开版本目录: {path}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            App.Logger.Error("Versions", "打开版本目录失败.", error);
            LauncherToasts.Error(L("versionSelector.openFolderFailed"));
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Version is not { IsValid: true } version) return;
        string gameDirectory = _gameDirectory;
        var dialog = new SaveFileDialog { Title = L("versionSettings.export"), Filter = L("versionSettings.scriptFilter"), DefaultExt = ".bat", FileName = Path.GetFileName(version.FolderPath) + ".bat" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await OperateAsync(() => App.GameLauncher.ExportScriptAsync(gameDirectory, version, dialog.FileName));
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (Version is not { IsValid: true } version) return;
        await OperateAsync(() =>
        {
            VersionManagementService.Repair(_gameDirectory, version);
            LauncherToasts.Information(L("versionSettings.repairQueued"));
            return Task.CompletedTask;
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Version is not { } version) return;
        string gameDirectory = _gameDirectory;
        int generation = _generation;
        await OperateAsync(async () =>
        {
            if (await VersionManagementService.DeleteAsync(gameDirectory, version) && generation == _generation) BackRequested?.Invoke();
        });
    }

    private async Task OperateAsync(Func<Task> operation)
    {
        int generation = _generation;
        ActionsPanel.IsEnabled = false;
        SidebarHost.IsEnabled = false;
        try
        {
            await operation();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            App.Logger.Error("Versions", "版本管理操作失败.", error);
            LauncherToasts.Error(string.Format(L("versionSettings.operationFailed"), error.Message));
        }
        finally
        {
            if (generation == _generation) ActionsPanel.IsEnabled = SidebarHost.IsEnabled = true;
        }
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
