using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Accounts;
using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Launch;

internal sealed record AccountRow(AccountEntry Account, bool IsActive, SkinAvatar? Avatar)
{
    public string UserName => Account.UserName;
    public Guid Uuid => Account.Uuid;
    public string KindText => Account.IsMicrosoft ? "Microsoft" : LocalizationManager.Get("account.offlineShort");
    public ImageSource? AvatarFace => Avatar?.Face;
    public ImageSource? AvatarHat => Avatar?.Hat;
}

/// <summary>管理离线账户, 同时为在线登录与皮肤功能保留界面位置.</summary>
public partial class AccountManagementView
{
    private bool _addFormOpen;
    private bool _microsoftPanelOpen;
    private bool _refreshingAccounts;
    private bool _appearanceVisible;
    private int _appearanceTransition;
    private CancellationTokenSource? _loginCancellation;
    private MicrosoftDeviceAuthorization? _deviceAuthorization;
    private string? _microsoftStatusKey;
    private object? _microsoftStatusArgument;
    private static readonly HttpClient MicrosoftHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly MicrosoftMinecraftAuthClient _microsoftAuth = new(MicrosoftHttpClient);

    public AccountManagementView()
    {
        InitializeComponent();
        AddForm.RenderTransform = new TranslateTransform();
        MicrosoftLoginPanel.RenderTransform = new TranslateTransform();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) _loginCancellation?.Cancel();
        };
        LocalizationManager.Changed += (_, _) =>
        {
            RefreshAccounts(AccountsList.SelectedItem is AccountRow row ? row.Uuid : App.Accounts.SelectedUuid);
            RefreshMicrosoftStatus();
        };
        App.Skins.AvatarChanged += uuid =>
        {
            if (App.Accounts.AllAccounts.Any(account => account.Uuid == uuid))
                RefreshAccounts(AccountsList.SelectedItem is AccountRow row ? row.Uuid : App.Accounts.SelectedUuid);
        };
        RefreshAccounts(App.Accounts.SelectedUuid);
    }

    private void RefreshAccounts(Guid? focusedUuid)
    {
        AccountRow[] rows = App.Accounts.AllAccounts
            .Select(account => new AccountRow(account, account.Uuid == App.Accounts.SelectedUuid, account.IsMicrosoft ? App.Skins.GetAvatar(account.Uuid) : null))
            .ToArray();
        _refreshingAccounts = true;
        try
        {
            AccountsList.ItemsSource = null;
            AccountsList.ItemsSource = rows;
            AccountCountText.Text = string.Format(LocalizationManager.Get("account.count"), rows.Length);
            EmptyHint.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            AccountsList.SelectedItem = rows.FirstOrDefault(row => row.Uuid == focusedUuid);
        }
        finally
        {
            _refreshingAccounts = false;
        }
        RefreshSelection();
    }

    internal void FocusCurrentAccount()
    {
        App.Logger.Information("Accounts", "已打开账户管理.");
        RefreshAccounts(App.Accounts.SelectedUuid);
    }

    private void SetMicrosoftStatus(string key, object? argument = null)
    {
        _microsoftStatusKey = key;
        _microsoftStatusArgument = argument;
        RefreshMicrosoftStatus();
    }

    private void RefreshMicrosoftStatus()
    {
        if (_microsoftStatusKey is null) return;
        string format = LocalizationManager.Get(_microsoftStatusKey);
        MicrosoftStatusText.Text = _microsoftStatusArgument is null ? format : string.Format(format, _microsoftStatusArgument);
    }

    private void RefreshSelection()
    {
        if (AccountsList.SelectedItem is not AccountRow row)
        {
            SetAppearanceVisible(false);
            SelectedNameText.Text = LocalizationManager.Get("account.noneSelected");
            SelectedInfoIcon.Visibility = Visibility.Collapsed;
            SelectedInfoIcon.ToolTip = null;
            UseButton.IsEnabled = DeleteButton.IsEnabled = false;
            return;
        }

        if (row.Account.IsMicrosoft)
        {
            AppearanceView.ShowAccount(row.Account);
            SetAppearanceVisible(true);
        }
        else SetAppearanceVisible(false);
        SelectedNameText.Text = row.UserName;
        SelectedInfoIcon.Visibility = Visibility.Visible;
        SelectedInfoIcon.ToolTip = row.Account.IsMicrosoft
            ? string.Format(LocalizationManager.Get("account.microsoftUuid"), row.Uuid)
            : string.Format(LocalizationManager.Get("account.offlineUuid"), row.Uuid);
        UseButton.IsEnabled = !row.IsActive;
        DeleteButton.IsEnabled = true;
    }

    private void AccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingAccounts) return;
        if (AccountsList.SelectedItem is AccountRow row)
            App.Logger.Information("Accounts", $"已选中账户: {row.UserName} ({row.Uuid:N}), 类型={(row.Account.IsMicrosoft ? "Microsoft" : "离线")}.");
        RefreshSelection();
    }

    private void SetAppearanceVisible(bool visible)
    {
        if (_appearanceVisible == visible) return;
        _appearanceVisible = visible;
        int transition = ++_appearanceTransition;
        AppearanceView.IsHitTestVisible = visible;
        if (visible) AppearanceHost.Visibility = Visibility.Visible;

        if (!IsLoaded)
        {
            AppearanceHost.Height = visible ? double.NaN : 0;
            AppearanceHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) AppearanceView.ShowAccount(null);
            return;
        }

        if (visible)
        {
            double width = AppearanceHost.ActualWidth > 0 ? AppearanceHost.ActualWidth : Math.Max(540, AccountsList.ActualWidth + 32);
            AppearanceView.Measure(new Size(width, double.PositiveInfinity));
        }

        // The second grid row stays at the bottom of the window. Growing its height
        // moves the drawer's top edge upward and gives the account list less room.
        double currentHeight = AppearanceHost.ActualHeight;
        AppearanceHost.BeginAnimation(HeightProperty, null);
        AppearanceHost.Height = currentHeight;
        var drawer = new DoubleAnimation(currentHeight, visible ? AppearanceView.DesiredSize.Height : 0,
            TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        drawer.Completed += (_, _) =>
        {
            if (transition != _appearanceTransition) return;
            AppearanceHost.BeginAnimation(HeightProperty, null);
            AppearanceHost.Height = _appearanceVisible ? double.NaN : 0;
            if (!_appearanceVisible)
            {
                AppearanceHost.Visibility = Visibility.Collapsed;
                AppearanceView.ShowAccount(null);
            }
        };
        AppearanceHost.BeginAnimation(HeightProperty, drawer);
    }

    private void AccountsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(AccountsList, source) is not ListBoxItem { DataContext: AccountRow row }) return;
        if (row.IsActive) return;

        SelectAccount(row);
        e.Handled = true;
    }

    private async void StartMicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_loginCancellation is not null) return;
        App.Logger.Information("Accounts", "开始 Microsoft 设备码登录.");
        CancellationTokenSource cancellation = new();
        _loginCancellation = cancellation;
        _deviceAuthorization = null;
        SetMicrosoftStatus("account.gettingCode");
        MicrosoftCodeText.Clear();
        CopyMicrosoftCodeButton.IsEnabled = OpenMicrosoftPageButton.IsEnabled = false;
        ShowMicrosoftPanel();
        try
        {
            string clientId = ServiceConfiguration.MicrosoftClientId;
            MicrosoftDeviceAuthorization authorization = await _microsoftAuth.StartDeviceAuthorizationAsync(clientId, cancellation.Token);
            _deviceAuthorization = authorization;
            App.Logger.Information("Accounts", "已获取 Microsoft 设备码, 等待用户授权.");
            MicrosoftCodeText.Text = authorization.UserCode;
            CopyMicrosoftCodeButton.IsEnabled = OpenMicrosoftPageButton.IsEnabled = true;
            bool copied = TryCopyCode();
            SetMicrosoftStatus(copied ? "account.codeCopiedOpened" : "account.enterCodeOpened");
            OpenMicrosoftPage();
            MicrosoftMinecraftSession session = await _microsoftAuth.CompleteDeviceAuthorizationAsync(clientId, authorization, cancellation.Token);
            App.Accounts.AddOrUpdateMicrosoft(session);
            App.Skins.RequestRefresh(session.Uuid, force: true);
            App.Logger.Information("Accounts", $"已添加 Microsoft 账户: {session.UserName}.");
            HideMicrosoftPanel();
            RefreshAccounts(session.Uuid);
            LauncherToasts.Success(string.Format(LocalizationManager.Get("account.signedIn"), session.UserName));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            App.Logger.Information("Accounts", "已取消 Microsoft 设备码登录.");
            HideMicrosoftPanel();
        }
        catch (Exception exception)
        {
            App.Logger.Error("Accounts", "Microsoft 账户登录失败.", exception);
            SetMicrosoftStatus("account.loginFailed", exception.Message);
            LauncherToasts.Error(LocalizationManager.Get("account.loginFailedHint"));
        }
        finally
        {
            _loginCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ShowMicrosoftPanel()
    {
        if (_microsoftPanelOpen) return;
        _microsoftPanelOpen = true;
        MicrosoftLoginPanel.Measure(new Size(MicrosoftLoginHost.ActualWidth, double.PositiveInfinity));
        AnimateHeight(MicrosoftLoginHost, MicrosoftLoginPanel.DesiredSize.Height, () => _microsoftPanelOpen);
        AnimatePanel(MicrosoftLoginPanel, 1, 0);
    }

    private void HideMicrosoftPanel()
    {
        if (!_microsoftPanelOpen) return;
        _microsoftPanelOpen = false;
        AnimateHeight(MicrosoftLoginHost, 0, () => _microsoftPanelOpen);
        AnimatePanel(MicrosoftLoginPanel, 0, -6);
    }

    private void CancelMicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        _loginCancellation?.Cancel();
        HideMicrosoftPanel();
    }

    private void CopyMicrosoftCode_Click(object sender, RoutedEventArgs e)
    {
        SetMicrosoftStatus(TryCopyCode() ? "account.codeCopied" : "account.copyFailed");
    }

    private bool TryCopyCode()
    {
        try
        {
            Clipboard.SetText(MicrosoftCodeText.Text);
            App.Logger.Information("Accounts", "Microsoft 设备码已复制到剪贴板.");
            return true;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            App.Logger.Error("Accounts", "复制 Microsoft 设备码失败.", exception);
            LauncherToasts.Error(LocalizationManager.Get("account.copyFailed"));
            return false;
        }
    }

    private void OpenMicrosoftPage_Click(object sender, RoutedEventArgs e) => OpenMicrosoftPage();

    private void OpenMicrosoftPage()
    {
        if (_deviceAuthorization is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_deviceAuthorization.VerificationUri.AbsoluteUri) { UseShellExecute = true });
            App.Logger.Information("Accounts", "已打开 Microsoft 设备码登录页面.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("Accounts", "打开 Microsoft 设备码登录页面失败.", exception);
            SetMicrosoftStatus("account.openFailed", _deviceAuthorization.VerificationUri);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("account.openFailed"), _deviceAuthorization.VerificationUri));
        }
    }

    private void ShowAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_addFormOpen)
        {
            UserNameBox.Focus();
            return;
        }

        _addFormOpen = true;
        App.Logger.Information("Accounts", "已打开添加离线账户表单.");
        ValidationText.Visibility = Visibility.Collapsed;
        AddForm.Measure(new Size(AddFormHost.ActualWidth, double.PositiveInfinity));
        AnimateFormHeight(AddForm.DesiredSize.Height);
        AnimateForm(1, 0);
        UserNameBox.Focus();
    }

    private void CancelAdd_Click(object sender, RoutedEventArgs e)
    {
        if (!_addFormOpen) return;
        _addFormOpen = false;
        App.Logger.Information("Accounts", "已关闭添加离线账户表单.");
        AnimateFormHeight(0);
        AnimateForm(0, -6);
        UserNameBox.Clear();
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void AnimateFormHeight(double targetHeight) => AnimateHeight(AddFormHost, targetHeight, () => _addFormOpen);

    private static void AnimateHeight(Border host, double targetHeight, Func<bool> isOpen)
    {
        double currentHeight = host.ActualHeight;
        host.BeginAnimation(HeightProperty, null);
        host.Height = currentHeight;
        var animation = new DoubleAnimation(currentHeight, targetHeight, TimeSpan.FromMilliseconds(230))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) =>
        {
            host.BeginAnimation(HeightProperty, null);
            host.Height = isOpen() ? double.NaN : 0;
        };
        host.BeginAnimation(HeightProperty, animation);
    }

    private void AnimateForm(double opacity, double offset) => AnimatePanel(AddForm, opacity, offset);

    private static void AnimatePanel(StackPanel panel, double opacity, double offset)
    {
        Duration duration = new(TimeSpan.FromMilliseconds(210));
        var fade = new DoubleAnimation(opacity, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        panel.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(offset, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }
        );
    }

    private void UserNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CreateOffline_Click(sender, e);
        else if (e.Key == Key.Escape) CancelAdd_Click(sender, e);
    }

    private void CreateOffline_Click(object sender, RoutedEventArgs e)
    {
        string name = UserNameBox.Text.Trim();
        try
        {
            if (!App.Accounts.TryAdd(name, out string error))
            {
                App.Logger.Warning("Accounts", $"添加离线账户未通过验证: {error}");
                ValidationText.Text = error;
                ValidationText.Visibility = Visibility.Visible;
                LauncherToasts.Error(error);
                return;
            }

            App.Logger.Information("Accounts", $"已添加离线账户: {name}.");
            CancelAdd_Click(sender, e);
            RefreshAccounts(App.Accounts.Accounts[^1].Uuid);
            LauncherToasts.Success(string.Format(LocalizationManager.Get("account.offlineAdded"), name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Accounts", "保存离线账户失败.", exception);
            LauncherToasts.Error($"{LocalizationManager.Get("account.addFailed")}: {exception.Message}");
        }
    }

    private void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is not AccountRow row) return;
        SelectAccount(row);
    }

    private void SelectAccount(AccountRow row)
    {
        try
        {
            App.Accounts.Select(row.Uuid);
            App.Logger.Information("Accounts", $"已切换账户: {row.UserName}.");
            RefreshAccounts(row.Uuid);
            LauncherToasts.Success(string.Format(LocalizationManager.Get("account.accountSelected"), row.UserName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Accounts", "切换账户失败.", exception);
            LauncherToasts.Error($"{LocalizationManager.Get("account.switchFailed")}: {exception.Message}");
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is not AccountRow row) return;
        try
        {
            if (!await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("account.deleteOfflineTitle"), string.Format(LocalizationManager.Get("account.confirmDelete"), row.UserName), confirmText: LocalizationManager.Get("common.delete"))) return;
            App.Accounts.Remove(row.Uuid);
            if (row.Account.IsMicrosoft) App.Skins.RemoveCache(row.Uuid);
            App.Logger.Information("Accounts", $"已删除账户: {row.UserName}.");
            RefreshAccounts(App.Accounts.SelectedUuid);
            LauncherToasts.Success(string.Format(LocalizationManager.Get("account.accountDeleted"), row.UserName));
        }
        catch (Exception exception)
        {
            App.Logger.Error("Accounts", "删除账户失败.", exception);
            LauncherToasts.Error($"{LocalizationManager.Get("account.deleteFailed")}: {exception.Message}");
        }
    }
}
