using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Accounts;
using ErhioLab.Craft.Launcher.Dialogs;

namespace ErhioLab.Craft.Launcher.Views.Launch;

internal sealed record AccountRow(OfflineAccount Account, bool IsActive)
{
    public string UserName => Account.UserName;
    public Guid Uuid => Account.Uuid;
}

/// <summary>管理离线账户，同时为在线登录与皮肤功能保留界面位置.</summary>
public partial class AccountManagementView
{
    private bool _addFormOpen;

    public AccountManagementView()
    {
        InitializeComponent();
        AddForm.RenderTransform = new TranslateTransform();
        RefreshAccounts(App.Accounts.SelectedUuid);
    }

    private void RefreshAccounts(Guid? focusedUuid)
    {
        AccountRow[] rows = App.Accounts.Accounts
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
        SelectedInfoIcon.ToolTip = $"离线账户 UUID: {row.Uuid:D}\n离线账户不验证用户名所有权。";
        UseButton.IsEnabled = !row.IsActive;
        DeleteButton.IsEnabled = true;
    }

    private void AccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshSelection();

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

    private void AnimateFormHeight(double targetHeight)
    {
        double currentHeight = AddFormHost.ActualHeight;
        AddFormHost.BeginAnimation(HeightProperty, null);
        AddFormHost.Height = currentHeight;
        var animation = new DoubleAnimation(currentHeight, targetHeight, TimeSpan.FromMilliseconds(230))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) =>
        {
            AddFormHost.BeginAnimation(HeightProperty, null);
            AddFormHost.Height = _addFormOpen ? double.NaN : 0;
        };
        AddFormHost.BeginAnimation(HeightProperty, animation);
    }

    private void AnimateForm(double opacity, double offset)
    {
        Duration duration = new(TimeSpan.FromMilliseconds(210));
        var fade = new DoubleAnimation(opacity, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        AddForm.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)AddForm.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
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
            if (!await LauncherDialogs.ConfirmAsync("删除离线账户", $"从列表中删除“{row.UserName}”？", confirmText: "删除")) return;
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
