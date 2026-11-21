using System.IO;
using System.Windows;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Versions;
using ErhioLab.Craft.Launcher.Views.Launch;

namespace ErhioLab.Craft.Launcher;

public partial class MainWindow
{
    private bool _versionSettingsFromSelector;

    private void InitializeVersionSettings()
    {
        VersionSelectionPage.VersionSettingsRequested += OpenVersionSettings;
        VersionSettingsPage.BackRequested += CloseVersionSettings;
        LocalizationManager.Changed += (_, _) => RefreshVersionSettingsTitle();
        VersionManagementService.Changed += async (oldPath, replacement) =>
        {
            bool selecting = ReferenceEquals(_activeRoute, _versionSelectionRoute);
            if (ReferenceEquals(_activeRoute, _versionSettingsRoute)
                && VersionManagementService.SamePath(VersionSettingsPage.Version?.FolderPath, oldPath))
            {
                if (replacement is null) CloseVersionSettings();
                else
                {
                    VersionSettingsPage.SetVersion(GameDirectoryOf(replacement), replacement);
                    RefreshVersionSettingsTitle();
                }
            }

            if (selecting) await VersionSelectionPage.ReloadAsync();
            if (_routes[ShellSection.Launch].Sidebar is LaunchSidebarView sidebar)
            {
                if (App.Settings.SelectedVersionId is { } id) sidebar.ShowSelectedVersion(id);
                else sidebar.ClearSelectedVersion();
            }
        };
    }

    internal async Task OpenSelectedVersionSettingsAsync()
    {
        string? path = App.Settings.SelectedVersionPath;
        string? game = App.Settings.SelectedGameFolderPath;
        if (path is null || game is null || App.FeatureVisibility.IsHidden(LauncherHiddenFeatures.VersionManagement)
            || !ReferenceEquals(_activeRoute, _routes[ShellSection.Launch])) return;
        int routeVersion = _routeVersion;
        try
        {
            var versions = await Task.Run(() => LocalMinecraftVersionScanner.Scan(game, includeInvalidVersions: true));
            if (routeVersion != _routeVersion || !PrimaryPageLayer.IsVisible || DialogOverlay.IsVisible
                || !VersionManagementService.SamePath(path, App.Settings.SelectedVersionPath)) return;
            var version = versions.FirstOrDefault(item => VersionManagementService.SamePath(item.FolderPath, path))
                          ?? throw new DirectoryNotFoundException(LocalizationManager.Get("versionSelector.directoryChanged"));
            OpenVersionSettings(version);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Versions", $"打开版本设置失败: {path}.", error);
            if (routeVersion == _routeVersion) LauncherToasts.Error(LocalizationManager.Get("versionSelector.readVersionsFailed"));
        }
    }

    internal void OpenVersionSettings(LocalMinecraftVersion version)
    {
        if (ReferenceEquals(_activeRoute, _versionSettingsRoute) || App.FeatureVisibility.IsHidden(LauncherHiddenFeatures.VersionManagement)) return;
        _versionSettingsFromSelector = ReferenceEquals(_activeRoute, _versionSelectionRoute);
        VersionSettingsPage.SetVersion(GameDirectoryOf(version), version);
        RefreshVersionSettingsTitle();
        ApplyShellRoute(_versionSettingsRoute);
        App.Logger.Information("Navigation", "打开版本设置.");
    }

    private static string GameDirectoryOf(LocalMinecraftVersion version) => Path.GetDirectoryName(Path.GetDirectoryName(version.FolderPath))!;

    private void RefreshVersionSettingsTitle()
    {
        if (VersionSettingsPage.Version is { } version)
            VersionSettingsTitleText.Text = string.Format(LocalizationManager.Get("versionSettings.title"), version.GameVersion ?? version.Id);
    }

    private void CloseVersionSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!VersionSettingsPage.TryCloseLog()) CloseVersionSettings();
    }

    private void CloseVersionSettings()
    {
        if (!ReferenceEquals(_activeRoute, _versionSettingsRoute)) return;
        if (_versionSettingsFromSelector) ShowVersionSelection(back: true);
        else ApplyShellRoute(_routes[ShellSection.Launch], back: true);
        App.Logger.Information("Navigation", "关闭版本设置.");
    }
}
