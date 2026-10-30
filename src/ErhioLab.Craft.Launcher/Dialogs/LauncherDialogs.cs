using System.Windows;

namespace ErhioLab.Craft.Launcher.Dialogs;

/// <summary>集中提供启动器的提示/警告/错误和二次确认弹窗.</summary>
internal static class LauncherDialogs
{
    /// <summary>显示普通提示.</summary>
    internal static void Information(string title, string message) =>
        _ = ShowAsync(title, message, LauncherDialogLevel.Information, false, "知道了");

    /// <summary>显示错误提示.</summary>
    internal static void Error(string title, string message) =>
        _ = ShowAsync(title, message, LauncherDialogLevel.Error, false, "知道了");

    /// <summary>显示警告提示.</summary>
    internal static void Warning(string title, string message) =>
        _ = ShowAsync(title, message, LauncherDialogLevel.Warning, false, "知道了");

    /// <summary>先显示二次确认, 确认后才执行调用方的操作.</summary>
    /// <returns>用户确认时为 true.</returns>
    internal static Task<bool> ConfirmAsync(string title, string message,
        LauncherDialogLevel level = LauncherDialogLevel.Warning,
        string confirmText = "确认") => ShowAsync(title, message, level, true, confirmText);

    private static async Task<bool> ShowAsync(string title, string message, LauncherDialogLevel level,
        bool requiresConfirmation,
        string confirmText)
    {
        if (Application.Current.MainWindow is MainWindow { IsVisible: true } mainWindow)
        {
            return await mainWindow.ShowDialogAsync(title, message, level, requiresConfirmation, confirmText);
        }

        LauncherDialogWindow dialog = new(title, message, level, requiresConfirmation, confirmText);
        return dialog.ShowDialog() == true;
    }
}
