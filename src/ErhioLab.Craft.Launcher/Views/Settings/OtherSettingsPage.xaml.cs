using System.IO;
using System.Text.Json;
using System.Windows;

using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Notifications;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>配置导入与导出页面.</summary>
public partial class OtherSettingsPage
{
    /// <summary>初始化其他设置页面.</summary>
    public OtherSettingsPage()
    {
        InitializeComponent();
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "导出启动器配置",
            Filter = "JSON 配置文件 (*.json)|*.json",
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
            LauncherToasts.Success("配置已导出.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Settings", "导出配置失败.", exception);
            LauncherDialogs.Error("导出配置失败", exception.Message);
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
            LauncherDialogs.Error("导入配置失败", exception.Message);
        }
    }

    private async Task ImportSettingsAsync()
    {
        OpenFileDialog dialog = new()
        {
            Title = "导入启动器配置",
            Filter = "JSON 配置文件 (*.json)|*.json",
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
            LauncherDialogs.Error("导入配置失败", $"无法使用所选配置文件: \n{exception.Message}");
            return;
        }

        await LauncherDialogs.WarningAsync("配置已导入", "配置已验证并保存. 点击\"知道了\"后, 启动器将自动重启以应用全部设置. ");
        try
        {
            App.Restart();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            App.Logger.Error("Settings", "导入配置后自动重启失败.", exception);
            LauncherDialogs.Error("自动重启失败", $"配置已保存, 请手动重启启动器: \n{exception.Message}");
        }
    }
}
