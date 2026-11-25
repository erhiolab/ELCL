using ErhioLab.Craft.Launcher.Localization;
using System.IO;
using System.ComponentModel;
using System.Security;
using System.Text.Json;
using System.Windows;

using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.SystemTools;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>隐私偏好与配置导入/导出页面.</summary>
public partial class OtherSettingsPage
{
    private bool _initializingPrivacy = true;
    /// <summary>初始化其他设置页面.</summary>
    public OtherSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshPrivacyControls();
        RefreshPrivacyControls();
        Loaded += (_, _) => RefreshFileAssociation();
        LocalizationManager.Changed += (_, _) => RefreshFileAssociation();
        RefreshFileAssociation();
    }

    private void RefreshFileAssociation()
    {
        try
        {
            var status = ModpackFileAssociation.Status();
            string key = status.DefaultHere ? "fileAssociation.defaultHere" : status.RegisteredHere ? "fileAssociation.available" : status.RegisteredElsewhere ? "fileAssociation.otherLocation" : "fileAssociation.notBound";
            AssociationStatusText.Text = LocalizationManager.Get(key);
            UnbindMrpackButton.IsEnabled = status.RegisteredHere;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            App.Logger.Error("FileAssociation", "读取文件关联状态失败.", error);
            AssociationStatusText.Text = LocalizationManager.Get("fileAssociation.statusFailed");
            UnbindMrpackButton.IsEnabled = false;
        }
    }

    private void BindMrpack_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("FileAssociation", "开始绑定 MRPACK 文件到当前 ELCL.");
        try
        {
            var status = ModpackFileAssociation.Register(LocalizationManager.Get("fileAssociation.fileType"));
            App.Logger.Information("FileAssociation", $"MRPACK 文件关联已注册, 当前默认={status.DefaultHere}.");
            LauncherToasts.Success(LocalizationManager.Get(status.DefaultHere ? "fileAssociation.bound" : "fileAssociation.chooseDefault"));
            RefreshFileAssociation();
            if (!status.DefaultHere) OpenDefaultApps();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException or ArgumentException) { AssociationError(error); }
    }

    private void UnbindMrpack_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("FileAssociation", "开始取消当前 ELCL 的 MRPACK 文件关联.");
        try
        {
            bool chooseDefault = ModpackFileAssociation.Unregister();
            App.Logger.Information("FileAssociation", $"MRPACK 文件关联已移除, 系统默认仍需选择={chooseDefault}.");
            LauncherToasts.Success(LocalizationManager.Get(chooseDefault ? "fileAssociation.unboundChoose" : "fileAssociation.unbound"));
            RefreshFileAssociation();
            if (chooseDefault) OpenDefaultApps();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException) { AssociationError(error); }
    }

    private void DefaultApps_Click(object sender, RoutedEventArgs e) => OpenDefaultApps();
    private static void OpenDefaultApps()
    {
        try
        {
            ModpackFileAssociation.OpenDefaultApps();
            App.Logger.Information("FileAssociation", "已打开 Windows 默认应用设置.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { AssociationError(error); }
    }
    private static void AssociationError(Exception error)
    {
        App.Logger.Error("FileAssociation", "文件关联操作失败.", error);
        LauncherToasts.Error(string.Format(LocalizationManager.Get("fileAssociation.failed"), error.Message));
    }

    private void RefreshPrivacyControls()
    {
        _initializingPrivacy = true;
        AnonymousAnalyticsBox.IsChecked = App.AnonymousAnalyticsEnabled;
        _initializingPrivacy = false;
    }

    private void AnonymousAnalytics_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingPrivacy) return;
        var enabled = AnonymousAnalyticsBox.IsChecked == true;
        if (enabled == App.AnonymousAnalyticsEnabled) return;
        App.Logger.Information("Privacy", $"开始保存匿名统计偏好: enabled={enabled}.");
        if (!App.UpdateAnalyticsConsent(enabled))
        {
            RefreshPrivacyControls();
            return;
        }
        App.Logger.Information("Privacy", $"匿名统计偏好已保存: enabled={enabled}.");
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e) => ExportBackup(accounts: false);

    private async void ExportAccounts_Click(object sender, RoutedEventArgs e)
    {
        var title = LocalizationManager.Get("settings.accountsExport");
        if (await LauncherDialogs.ConfirmAsync(title, LocalizationManager.Get("settings.accountsExportWarning"), confirmText: title))
            ExportBackup(accounts: true);
    }

    private void ExportBackup(bool accounts)
    {
        SaveFileDialog dialog = new()
        {
            Title = LocalizationManager.Get(accounts ? "settings.accountsExport" : "settings.export"),
            Filter = LocalizationManager.Get("settings.backupFileFilter"),
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"ELCL-{(accounts ? "accounts" : "settings")}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            App.Logger.Information("Settings", $"开始导出{(accounts ? "账户信息" : "启动器配置")}: {dialog.FileName}.");
            if (accounts)
            {
                App.Accounts.ExportBackup(dialog.FileName);
                App.Logger.Information("Settings", $"账户信息已导出: {dialog.FileName}.");
            }
            else App.ExportSettings(dialog.FileName);
            LauncherToasts.Success(LocalizationManager.Get(accounts ? "settings.accountsExported" : "settings.exported"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            App.Logger.Error("Settings", "导出备份失败.", exception);
            var title = LocalizationManager.Get(accounts ? "settings.accountsExportFailed" : "settings.exportFailed");
            LauncherToasts.Error(string.Format(LocalizationManager.Get("settings.backupFailure"), title, exception.Message));
        }
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e) => await ImportBackupAsync(accounts: false);

    private async void ImportAccounts_Click(object sender, RoutedEventArgs e) => await ImportBackupAsync(accounts: true);

    private async Task ImportBackupAsync(bool accounts)
    {
        OpenFileDialog dialog = new()
        {
            Title = LocalizationManager.Get(accounts ? "settings.accountsImport" : "settings.import"),
            Filter = LocalizationManager.Get("settings.backupFileFilter"),
            DefaultExt = ".json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            App.Logger.Information("Settings", $"开始读取{(accounts ? "账户" : "配置")}备份: {dialog.FileName}.");
            if (accounts)
            {
                var result = App.Accounts.ImportBackup(dialog.FileName);
                App.Logger.Information("Settings", $"账户备份已处理, 新增 {result.Added} 个, 无法恢复 {result.UnavailableMicrosoft} 个 Microsoft 账户.");
                var message = result.Added == 0 ? LocalizationManager.Get("settings.accountsNoNew")
                    : string.Format(LocalizationManager.Get("settings.accountsImported"), result.Added);
                if (result.UnavailableMicrosoft > 0)
                    LauncherToasts.Information($"{message} {string.Format(LocalizationManager.Get("settings.accountsUnavailable"), result.UnavailableMicrosoft)}");
                else LauncherToasts.Success(message);
                return;
            }

            var backup = App.ReadSettingsBackup(dialog.FileName);
            if (!await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("settings.import"), LocalizationManager.Get("settings.importConfirm"))) return;
            App.ImportSettings(backup);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
            App.Logger.Error("Settings", "导入备份失败.", exception);
            var title = LocalizationManager.Get(accounts ? "settings.accountsImportFailed" : "settings.importFailed");
            LauncherToasts.Error(string.Format(LocalizationManager.Get("settings.backupFailure"), title, exception.Message));
            return;
        }

        await LauncherDialogs.WarningAsync(LocalizationManager.Get("settings.imported"), LocalizationManager.Get("settings.importRestartMessage"));
        try
        {
            App.Restart();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            App.Logger.Error("Settings", "导入配置后自动重启失败.", exception);
            LauncherDialogs.Error(LocalizationManager.Get("settings.automaticRestartFailed"), string.Format(LocalizationManager.Get("settings.restartFailed"), exception.Message));
        }
    }
}
