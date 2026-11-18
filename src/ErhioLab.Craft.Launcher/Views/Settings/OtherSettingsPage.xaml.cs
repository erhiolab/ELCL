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
        AnonymousAnalyticsBox.IsChecked = App.Settings.AnonymousAnalyticsEnabled;
        _initializingPrivacy = false;
    }

    private void AnonymousAnalytics_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingPrivacy) return;
        var enabled = AnonymousAnalyticsBox.IsChecked == true;
        if (enabled == App.Settings.AnonymousAnalyticsEnabled) return;
        App.Logger.Information("Privacy", $"开始保存匿名统计偏好: enabled={enabled}.");
        if (!App.UpdateSettings(App.Settings with { AnonymousAnalyticsEnabled = enabled }))
        {
            RefreshPrivacyControls();
            return;
        }
        App.Logger.Information("Privacy", $"匿名统计偏好已保存: enabled={enabled}.");
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = LocalizationManager.Get("settings.exportDialogTitle"),
            Filter = LocalizationManager.Get("settings.jsonFileFilter"),
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            App.ExportSettings(dialog.FileName);
            LauncherToasts.Success(LocalizationManager.Get("settings.exported"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Settings", "导出配置失败.", exception);
            LauncherDialogs.Error(LocalizationManager.Get("settings.exportFailed"), exception.Message);
        }
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ImportSettingsAsync();
        }
        catch (Exception exception)
        {
            App.Logger.Error("Settings", "导入配置时发生未预期的错误.", exception);
            LauncherDialogs.Error(LocalizationManager.Get("settings.importFailed"), exception.Message);
        }
    }

    private async Task ImportSettingsAsync()
    {
        OpenFileDialog dialog = new()
        {
            Title = LocalizationManager.Get("settings.importDialogTitle"),
            Filter = LocalizationManager.Get("settings.jsonFileFilter"),
            DefaultExt = ".json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            App.ImportSettings(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            App.Logger.Error("Settings", "导入配置失败.", exception);
            LauncherDialogs.Error(LocalizationManager.Get("settings.importFailed"), string.Format(LocalizationManager.Get("settings.invalidImport"), exception.Message));
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
