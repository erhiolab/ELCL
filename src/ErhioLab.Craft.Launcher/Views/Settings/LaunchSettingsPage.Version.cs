using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Versions;

namespace ErhioLab.Craft.Launcher.Views.Settings;

public partial class LaunchSettingsPage
{
    private string? _versionDirectory;
    private string? _versionGameDirectory;
    private LocalMinecraftVersion? _editingVersion;
    private VersionLaunchOptions _versionOptions = new();
    private bool _versionLoadFailed;
    private readonly HashSet<TextBox> _dirtyLaunchText = [];
    private bool IsVersionMode => _versionDirectory is not null;
    private LauncherSettings CurrentSettings => IsVersionMode ? _versionOptions.Apply(App.Settings) : App.Settings;

    internal void SetVersionContext(string gameDirectory, LocalMinecraftVersion version)
    {
        FlushPendingChanges();
        _versionDirectory = version.FolderPath;
        _versionGameDirectory = gameDirectory;
        _editingVersion = version;
        _versionOptions = new();
        _versionLoadFailed = false;
        try
        {
            _versionOptions = (new VersionSettingsStore(version.FolderPath).Load().LaunchOptions ?? new()).Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _versionLoadFailed = true;
            App.Logger.Error("Settings", $"读取版本启动设置失败, 保留原文件: {version.FolderPath}.", error);
            LauncherToasts.Error(LocalizationManager.Get("versionLaunch.readFailed"));
        }

        IsEnabled = !_versionLoadFailed;
        VersionJavaPanel.Visibility = Visibility.Visible;
        JavaCatalogCard.Visibility = Visibility.Collapsed;
        _placeholderLoadRevision++;
        _placeholderVersionFolder = version.FolderPath;
        _placeholderVersion = version;
        RefreshLaunchOptions();
        RefreshSpecifiedJava();
        App.Logger.Information("Settings", $"打开版本启动设置: {version.FolderPath}.");
    }

    internal void FlushPendingChanges()
    {
        CommitLaunchText();
        CommitMemoryPercentage();
        if (CurrentSettings.WindowMode == GameWindowMode.Custom)
            WindowSizeBox_LostFocus(GameWindowWidthBox, new RoutedEventArgs());
    }

    private bool SaveVersionOptions(VersionLaunchOptions options)
    {
        if (_versionDirectory is null || _versionLoadFailed) return false;
        try
        {
            _versionOptions = new VersionSettingsStore(_versionDirectory).SaveLaunchOptions(options, App.Settings);
            App.Logger.Information("Settings", $"已保存版本启动设置: {_versionDirectory}.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Logger.Error("Settings", $"保存版本启动设置失败: {_versionDirectory}.", error);
            LauncherToasts.Error(LocalizationManager.Get("versionLaunch.saveFailed"));
            RefreshLaunchOptions();
            return false;
        }
    }

    private void RefreshVersionControls()
    {
        if (!IsVersionMode) return;
        JavaModeBox.ItemsSource = new[] { LocalizationManager.Get("versionLaunch.javaAutomatic"), LocalizationManager.Get("versionLaunch.javaSpecified") };
        JavaModeBox.SelectedIndex = _versionOptions.UseSpecifiedJava ? 1 : 0;
        SpecifiedJavaReveal.IsExpanded = _versionOptions.UseSpecifiedJava;
    }

    private void RefreshSpecifiedJava()
    {
        if (!IsVersionMode) return;
        bool initializing = _initializingLaunchOptions;
        _initializingLaunchOptions = true;
        // 已移除的指定路径仍保留显示, 避免刷新列表时静默改用其他 Java.
        List<JavaInstallation> choices = App.Settings.JavaInstallations.Select(java => java.ToJava()).ToList();
        if (_versionOptions.JavaPath is { } path && !choices.Any(java => string.Equals(java.Path, path, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new JavaInstallation(path, "", 0, ""));
        SpecifiedJavaBox.ItemsSource = choices;
        SpecifiedJavaBox.SelectedItem = choices.FirstOrDefault(java => string.Equals(java.Path, _versionOptions.JavaPath, StringComparison.OrdinalIgnoreCase));
        RefreshVersionJavaButton.IsEnabled = !App.JavaCatalog.IsRefreshing;
        _initializingLaunchOptions = initializing;
    }

    private void JavaModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLaunchOptions || !IsVersionMode || JavaModeBox.SelectedIndex < 0) return;
        bool specified = JavaModeBox.SelectedIndex == 1;
        string? path = specified ? _versionOptions.JavaPath ?? App.Settings.JavaInstallations.FirstOrDefault()?.Path : null;
        if (!SaveVersionOptions(_versionOptions with { UseSpecifiedJava = specified, JavaPath = path })) return;
        SpecifiedJavaReveal.IsExpanded = specified;
        RefreshSpecifiedJava();
        UpdateLaunchPreviews();
    }

    private void SpecifiedJavaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLaunchOptions || !IsVersionMode || SpecifiedJavaBox.SelectedItem is not JavaInstallation java) return;
        SaveVersionOptions(_versionOptions with { UseSpecifiedJava = true, JavaPath = java.Path });
        UpdateLaunchPreviews();
    }

    internal void ResetVersionOptions()
    {
        if (!IsVersionMode || _versionLoadFailed) return;
        App.Logger.Information("Settings", $"开始恢复版本启动设置: {_versionDirectory}.");
        _launchTextSaveTimer.Stop();
        _dirtyLaunchText.Clear();
        if (!SaveVersionOptions(new VersionLaunchOptions { AdditionalData = _versionOptions.AdditionalData })) return;
        RefreshLaunchOptions();
        RefreshSpecifiedJava();
        App.Logger.Information("Settings", $"版本启动设置已恢复为全局设置: {_versionDirectory}.");
        LauncherToasts.Success(LocalizationManager.Get("versionLaunch.resetDone"));
    }
}
