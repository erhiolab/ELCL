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

namespace ErhioLab.Craft.Launcher.Views.Launch;

internal sealed record AccountRow(AccountEntry Account, bool IsActive)
{
    public string UserName => Account.UserName;
    public Guid Uuid => Account.Uuid;
    public string KindText => Account.IsMicrosoft ? "Microsoft" : "离线";
}

/// <summary>管理离线账户, 同时为在线登录与皮肤功能保留界面位置.</summary>
public partial class AccountManagementView
{
    private bool _addFormOpen;
    private bool _microsoftPanelOpen;
    private CancellationTokenSource? _loginCancellation;
    private MicrosoftDeviceAuthorization? _deviceAuthorization;
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
        RefreshAccounts(App.Accounts.SelectedUuid);
    }

    private void RefreshAccounts(Guid? focusedUuid)
    {
        AccountRow[] rows = App.Accounts.AllAccounts
            .Select(account => new AccountRow(account, account.Uuid == App.Accounts.SelectedUuid))
            .ToArray();
        AccountsList.ItemsSource = null;
        AccountsList.ItemsSource = rows;
        AccountCountText.Text = $"{rows.Length} 个账户";
        EmptyHint.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountsList.SelectedItem = rows.FirstOrDefault(row => row.Uuid == focusedUuid);
        RefreshSelection();
    }

    internal void FocusCurrentAccount() => RefreshAccounts(App.Accounts.SelectedUuid);

    private void RefreshSelection()
    {
        if (AccountsList.SelectedItem is not AccountRow row)
        {
            SelectedNameText.Text = "尚未选择";
            SelectedInfoIcon.Visibility = Visibility.Collapsed;
            SelectedInfoIcon.ToolTip = null;
            UseButton.IsEnabled = DeleteButton.IsEnabled = false;
            return;
        }

        SelectedNameText.Text = row.UserName;
        SelectedInfoIcon.Visibility = Visibility.Visible;
        SelectedInfoIcon.ToolTip = row.Account.IsMicrosoft
            ? $"Microsoft 账户 UUID: {row.Uuid:D}"
            : $"离线账户 UUID: {row.Uuid:D}\n离线账户不验证用户名所有权. ";
        UseButton.IsEnabled = !row.IsActive;
        DeleteButton.IsEnabled = true;
    }

    private void AccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshSelection();

    private async void StartMicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_loginCancellation is not null) return;
        CancellationTokenSource cancellation = new();
        _loginCancellation = cancellation;
        _deviceAuthorization = null;
        MicrosoftStatusText.Text = "正在获取登录代码...";
        MicrosoftCodeText.Clear();
        CopyMicrosoftCodeButton.IsEnabled = OpenMicrosoftPageButton.IsEnabled = false;
        ShowMicrosoftPanel();
        try
        {
            string clientId = App.Services.MicrosoftClientId;
            MicrosoftDeviceAuthorization authorization = await _microsoftAuth.StartDeviceAuthorizationAsync(clientId, cancellation.Token);
            _deviceAuthorization = authorization;
            MicrosoftCodeText.Text = authorization.UserCode;
            CopyMicrosoftCodeButton.IsEnabled = OpenMicrosoftPageButton.IsEnabled = true;
            bool copied = TryCopyCode();
            MicrosoftStatusText.Text = copied
                ? "代码已复制. 请在打开的网页中粘贴并完成授权. "
                : "请复制代码, 在打开的网页中输入并完成授权. ";
            OpenMicrosoftPage();
            MicrosoftMinecraftSession session = await _microsoftAuth.CompleteDeviceAuthorizationAsync(clientId, authorization, cancellation.Token);
            App.Accounts.AddOrUpdateMicrosoft(session);
            App.Logger.Information("Accounts", $"已添加 Microsoft 账户: {session.UserName}.");
            HideMicrosoftPanel();
            RefreshAccounts(session.Uuid);
            LauncherToasts.Success($"已登录 Microsoft 账户 {session.UserName}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            HideMicrosoftPanel();
        }
        catch (Exception exception)
        {
            App.Logger.Error("Accounts", "Microsoft 账户登录失败.", exception);
            MicrosoftStatusText.Text = $"登录失败: {exception.Message}";
            LauncherToasts.Error("Microsoft 登录失败, 请查看账户管理中的提示. ");
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
        MicrosoftStatusText.Text = TryCopyCode() ? "代码已复制. 请在网页中完成授权. " : "无法访问剪贴板, 请手动选中并复制代码. ";
    }

    private bool TryCopyCode()
    {
        try
        {
            Clipboard.SetText(MicrosoftCodeText.Text);
            return true;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
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
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MicrosoftStatusText.Text = "无法自动打开浏览器, 请在浏览器中访问 " + _deviceAuthorization.VerificationUri;
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
                ValidationText.Text = error;
                ValidationText.Visibility = Visibility.Visible;
                return;
            }

            App.Logger.Information("Accounts", $"已添加离线账户: {name}.");
            CancelAdd_Click(sender, e);
            RefreshAccounts(App.Accounts.Accounts[^1].Uuid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Accounts", "保存离线账户失败.", exception);
            LauncherDialogs.Error("添加账户失败", exception.Message);
        }
    }

    private void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is not AccountRow row) return;
        try
        {
            App.Accounts.Select(row.Uuid);
            App.Logger.Information("Accounts", $"已切换为离线账户: {row.UserName}.");
            RefreshAccounts(row.Uuid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Accounts", "切换账户失败.", exception);
            LauncherDialogs.Error("切换账户失败", exception.Message);
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is not AccountRow row) return;
        try
        {
            if (!await LauncherDialogs.ConfirmAsync("删除离线账户", $"从列表中删除"{row.UserName}"? ", confirmText: "删除")) return;
            App.Accounts.Remove(row.Uuid);
            App.Logger.Information("Accounts", $"已删除离线账户: {row.UserName}.");
            RefreshAccounts(App.Accounts.SelectedUuid);
        }
        catch (Exception exception)
        {
            App.Logger.Error("Accounts", "删除账户失败.", exception);
            LauncherDialogs.Error("删除账户失败", exception.Message);
        }
    }
}
