using System.Windows;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Views.Launch;

/// <summary>显示账户管理入口和游戏版本入口.</summary>
public partial class LaunchSidebarView
{
    /// <summary>初始化启动页侧栏.</summary>
    public LaunchSidebarView()
    {
        InitializeComponent();
        App.GameLauncher.StateChanged += RefreshLaunchState;
        App.Accounts.Changed += (_, _) => RefreshAccount();
        App.Skins.AvatarChanged += uuid =>
        {
            if (App.Accounts.SelectedUuid == uuid) RefreshAccount();
        };
        LocalizationManager.Changed += (_, _) =>
        {
            RefreshAccount();
            if (App.Settings.SelectedVersionId is { Length: > 0 } selectedVersionId) ShowSelectedVersion(selectedVersionId);
            else ClearSelectedVersion();
        };
        RefreshAccount();
        if (App.Settings.SelectedVersionId is { Length: > 0 } id)
        {
            ShowSelectedVersion(id);
        }
    }

    private void RefreshAccount()
    {
        var account = App.Accounts.SelectedAccount;
        var avatar = account?.IsMicrosoft == true ? App.Skins.GetAvatar(account.Uuid) : null;
        AccountAvatarFaceImage.Source = avatar?.Face;
        AccountAvatarHatImage.Source = avatar?.Hat;
        AccountNameText.Text = account?.UserName ?? LocalizationManager.Get("launch.notSignedIn");
        AccountDescriptionText.Text = account is null ? LocalizationManager.Get("launch.manageAccounts")
            : account.IsMicrosoft ? LocalizationManager.Get("launch.microsoftAccount") : LocalizationManager.Get("launch.offlineAccount");
    }

    internal void ShowSelectedVersion(string id)
    {
        MainActionTitle.Text = id;
        RefreshLaunchState();
    }

    private void RefreshLaunchState()
    {
        MainActionButton.IsEnabled = !App.GameLauncher.IsPreparing;
        MainActionDescription.Text = LocalizationManager.Get(App.GameLauncher.IsPreparing ? "gameLaunch.preparingShort" :
            App.Settings.SelectedVersionId is not null ? "gameLaunch.clickToLaunch" : "launch.noInstance");
    }

    internal void ClearSelectedVersion()
    {
        MainActionTitle.Text = LocalizationManager.Get("launch.downloadVersion");
        RefreshLaunchState();
    }

    private void AccountManagement_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow window)
        {
            window.OpenAccountManagement();
        }
    }

    private async void DownloadVersion_Click(object sender, RoutedEventArgs e)
    {
        if (App.Settings.SelectedVersionId is not null)
        {
            await App.GameLauncher.StartAsync();
            return;
        }

        App.Logger.Information("Launch", "点击下载游戏版本入口.");
        if (Application.Current.MainWindow is MainWindow window)
        {
            window.OpenDownloadPage();
        }
    }

    private void VersionSelection_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Launch", "点击版本选择入口.");
        if (Application.Current.MainWindow is MainWindow window)
        {
            window.OpenVersionSelection();
        }
    }
}
