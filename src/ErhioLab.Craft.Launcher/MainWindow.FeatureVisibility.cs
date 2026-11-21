using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Views.Launch;
using ErhioLab.Craft.Launcher.Views.Shared;

namespace ErhioLab.Craft.Launcher;

public partial class MainWindow
{
    private void InitializeFeatureVisibility()
    {
        App.FeatureVisibility.Changed += ApplyFeatureVisibility;
        InputManager input = InputManager.Current;
        input.PreProcessInput += FeatureVisibility_PreProcessInput;
        Closed += (_, _) =>
        {
            input.PreProcessInput -= FeatureVisibility_PreProcessInput;
            App.FeatureVisibility.Changed -= ApplyFeatureVisibility;
        };
        ApplyFeatureVisibility();
    }

    private static void FeatureVisibility_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        // 同一 WPF 调度器的页面、弹窗和弹出控件均可接收此快捷键, 不注册系统全局热键.
        if (e.StagingItem.Input is not KeyEventArgs { RoutedEvent: var routedEvent, Key: Key.F12 } key
            || routedEvent != Keyboard.PreviewKeyDownEvent) return;

        key.Handled = true;
        if (!key.IsRepeat) App.FeatureVisibility.ToggleTemporaryReveal();
    }

    private void ApplyFeatureVisibility()
    {
        var features = App.FeatureVisibility;
        bool settingsAvailable = ((SectionSidebarView)_routes[ShellSection.Settings].Sidebar).ApplyFeatureVisibility(features);
        bool moreAvailable = ((SectionSidebarView)_routes[ShellSection.More].Sidebar).ApplyFeatureVisibility(features);
        bool downloadVisible = !features.IsHidden(LauncherHiddenFeatures.Download);
        bool versionManagementVisible = !features.IsHidden(LauncherHiddenFeatures.VersionManagement);

        DownloadNavigation.Visibility = ToVisibility(downloadVisible);
        SettingsNavigation.Visibility = ToVisibility(settingsAvailable && !features.IsHidden(LauncherHiddenFeatures.Settings));
        MoreNavigation.Visibility = ToVisibility(moreAvailable && !features.IsHidden(LauncherHiddenFeatures.More));
        Resources["VersionManagementVisibility"] = ToVisibility(versionManagementVisible);
        ((LaunchSidebarView)_routes[ShellSection.Launch].Sidebar).ApplyFeatureVisibility(downloadVisible, versionManagementVisible);

        if (!versionManagementVisible && ReferenceEquals(_activeRoute, _versionSettingsRoute)) CloseVersionSettings();
        if (_selectedNavigationButton?.Visibility == Visibility.Collapsed) LaunchNavigation.IsChecked = true;
        UpdateShellRouteTitle();

        if (TopNavigationHost.IsVisible && _navigationIndicatorReady)
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (TopNavigationHost.IsVisible && _selectedNavigationButton is { } button) MoveNavigationIndicator(button, animate: true);
            }, DispatcherPriority.Loaded);
    }

    private bool HasExtraPrimaryPages => DownloadNavigation.Visibility == Visibility.Visible || SettingsNavigation.Visibility == Visibility.Visible || MoreNavigation.Visibility == Visibility.Visible;

    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
