using System.IO;
using System.Windows;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Versions;
using ErhioLab.Craft.Launcher.Views.Launch;

namespace ErhioLab.Craft.Launcher;

public partial class MainWindow
{
    private bool _versionSettingsFromSelector;
    private bool _versionSettingsClosing;

    private void InitializeVersionSettings()
    {
        VersionSelectionPage.VersionSettingsRequested += OpenVersionSettings;
        VersionSettingsPage.BackRequested += CloseVersionSettings;
        LocalizationManager.Changed += (_, _) => RefreshVersionSettingsTitle();
        VersionManagementService.Changed += async (oldPath, replacement) =>
        {
            if (VersionManagementService.SamePath(VersionSettingsPage.Version?.FolderPath, oldPath))
            {
                if (replacement is null) CloseVersionSettings();
                else
                {
                    VersionSettingsPage.SetVersion(GameDirectoryOf(replacement), replacement);
                    RefreshVersionSettingsTitle();
                }
            }

            if (VersionSelectionPage.Visibility == Visibility.Visible) await VersionSelectionPage.ReloadAsync();
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
        if (path is null || game is null) return;
        try
        {
            var versions = await Task.Run(() => LocalMinecraftVersionScanner.Scan(game, includeInvalidVersions: true));
            var version = versions.FirstOrDefault(item => VersionManagementService.SamePath(item.FolderPath, path))
                          ?? throw new DirectoryNotFoundException(LocalizationManager.Get("versionSelector.directoryChanged"));
            OpenVersionSettings(version);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Versions", $"打开版本设置失败: {path}.", error);
            LauncherToasts.Error(LocalizationManager.Get("versionSelector.readVersionsFailed"));
        }
    }

    internal void OpenVersionSettings(LocalMinecraftVersion version)
    {
        if (_versionSettingsClosing) return;
        _versionSettingsFromSelector = VersionSelectionPage.Visibility == Visibility.Visible;
        VersionSettingsPage.SetVersion(GameDirectoryOf(version), version);
        RefreshVersionSettingsTitle();
        VersionSelectionTitle.Visibility = Visibility.Collapsed;
        if (_versionSettingsFromSelector) VersionSelectionPage.IsEnabled = false;
        OpenSecondaryPage(VersionSettingsPage, VersionSettingsTitle, "版本设置");
        UpdateTaskManagerButton();
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
        if (_versionSettingsClosing || VersionSettingsPage.Visibility != Visibility.Visible) return;
        _versionSettingsClosing = true;
        CloseSecondaryPage(VersionSettingsPage, VersionSettingsTitle, "版本设置", () =>
        {
            _versionSettingsClosing = false;
            VersionSelectionPage.IsEnabled = true;
            if (_versionSettingsFromSelector && VersionSelectionPage.Visibility == Visibility.Visible)
            {
                TitleBarLabel.Visibility = TopNavigationHost.Visibility = Visibility.Collapsed;
                VersionSelectionTitle.Visibility = Visibility.Visible;
            }
        });
    }
}
