using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Icons;
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
    internal LocalMinecraftVersion? Version { get; private set; }
    private string _gameDirectory = "";
    private int _generation;
    private readonly LaunchSettingsPage _launchSettingsPage;
    internal event Action? BackRequested;

    public VersionSettingsView()
    {
        InitializeComponent();
        _launchSettingsPage = new LaunchSettingsPage();
        PageHost.Content = null;
        var pageHost = new AnimatedPageHost(OverviewPage);
        PageHost.Content = pageHost;
        var overview = new SectionSidebarItem("versionSettings.overview", LauncherIcons.GameVersions);
        var settings = new SectionSidebarItem("versionSettings.settings", LauncherIcons.Settings, CanReset: true, ActionToolTipKey: "versionLaunch.reset");
        var sidebar = new SectionSidebarView(overview, settings);
        SidebarHost.Content = sidebar;
        sidebar.SelectionChanged += item =>
        {
            _launchSettingsPage.FlushPendingChanges();
            if (ReferenceEquals(item, settings) && Version is { } version)
            {
                _launchSettingsPage.SetVersionContext(_gameDirectory, version);
                pageHost.Navigate(_launchSettingsPage);
            }
            else pageHost.Navigate(OverviewPage, back: true);
        };
        sidebar.ActionRequested += item =>
        {
            if (ReferenceEquals(item, settings)) _launchSettingsPage.ResetVersionOptions();
        };
        Unloaded += (_, _) => _launchSettingsPage.FlushPendingChanges();
        LocalizationManager.Changed += (_, _) => RefreshSummary();
    }

    internal void SetVersion(string gameDirectory, LocalMinecraftVersion version)
    {
        _launchSettingsPage.SetVersionContext(gameDirectory, version);
        _gameDirectory = Path.GetFullPath(gameDirectory);
        Version = version;
        _generation++;
        ActionsPanel.IsEnabled = true;
        SidebarHost.IsEnabled = true;
        RenameReveal.IsExpanded = false;
        DescriptionReveal.IsExpanded = false;
        RefreshSummary();
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
