using System.IO;
using System.Text.Json;
using System.Windows;

using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Styles;

namespace ErhioLab.Craft.Launcher;

public partial class App
{
    private bool TryLoadStartupSettings(LauncherDataFile<LauncherSettings> store)
    {
        var previousMode = ShutdownMode;
        // 配置提示早于主窗口, 关闭提示不能使 WPF 提前关闭整个应用.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var loaded = LoadOrResetStartupSettings(store, ConfirmUnversionedSettingsReset);
            if (loaded is null) return false;
            Settings = loaded;
            LocalizationManager.Apply(Settings.Language);
            Logger.Information("Settings", $"配置已加载, 主题: {Settings.Theme}, 主题色: {Settings.AccentColor}, 不透明度: {Settings.WindowOpacity:P0}, 内存单位: {Settings.MemoryUnit}, 版本列表源: {Settings.VersionListSource}, 最大连接数: {Settings.DownloadMaxConnections}, 限速: {Settings.DownloadSpeedLimitMiBPerSecond} MiB/s.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            Logger.Error("Settings", $"无法加载主配置, 已停止启动: {store.FilePath}.", error);
            LocalizationManager.Apply(Settings.Language);
            LauncherThemeManager.Apply(Settings);
            var dialog = new LauncherDialogWindow(LocalizationManager.Get("app.startFailed"),
                string.Format(LocalizationManager.Get("settings.startupConfigFailed"), store.FilePath, error.Message),
                LauncherDialogLevel.Error, false, LocalizationManager.Get("settings.exitLauncher"));
            dialog.ShowInTaskbar = true;
            dialog.ShowDialog();
            return false;
        }
        finally
        {
            ShutdownMode = previousMode;
        }
    }

    private static bool ConfirmUnversionedSettingsReset(string path)
    {
        LocalizationManager.Apply(Settings.Language);
        LauncherThemeManager.Apply(Settings);
        var dialog = new LauncherDialogWindow(LocalizationManager.Get("settings.startupConfigUnversioned"),
            string.Format(LocalizationManager.Get("settings.startupConfigResetPrompt"), path),
            LauncherDialogLevel.Warning, true, LocalizationManager.Get("settings.startupConfigReset"));
        dialog.ConfigureDecision(LocalizationManager.Get("settings.exitLauncher"), longForm: false);
        return dialog.ShowDialog() == true;
    }

    private static LauncherSettings? LoadOrResetStartupSettings(LauncherDataFile<LauncherSettings> store, Func<string, bool> confirmReset)
    {
        var defaults = new LauncherSettings();
        try
        {
            return store.Load(defaults).Normalize();
        }
        catch (LauncherConfigVersionMissingException error)
        {
            Logger.Error("Settings", $"主配置没有格式版本, 等待用户选择: {store.FilePath}.", error);
            if (!confirmReset(store.FilePath))
            {
                Logger.Information("Settings", "已取消主配置重建, 启动器将退出.");
                return null;
            }
        }

        // 等待确认期间文件可能已被替换, 只备份仍然没有版本的文件.
        try
        {
            return store.Load(defaults).Normalize();
        }
        catch (LauncherConfigVersionMissingException)
        {
            string backupName = $"config.unversioned-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json";
            string backupPath = Path.Combine(Path.GetDirectoryName(store.FilePath)!, backupName);
            File.Move(store.FilePath, backupPath, overwrite: false);
            Logger.Information("Settings", $"旧主配置已保留为备份, 将使用默认配置启动: {backupPath}.");
            return defaults.Normalize();
        }
    }
}
