using System.Windows;

namespace ErhioLab.Craft.Launcher.Notifications;

/// <summary>从启动器界面的任意位置发出非阻塞通知.</summary>
internal static class LauncherToasts
{
    /// <summary>弹出蓝色信息通知.</summary>
    internal static void Information(string message) => Show(new ToastNotification(message, ToastLevel.Information));

    /// <summary>弹出绿色成功通知.</summary>
    internal static void Success(string message) => Show(new ToastNotification(message, ToastLevel.Success));

    /// <summary>弹出红色错误通知.</summary>
    internal static void Error(string message) => Show(new ToastNotification(message, ToastLevel.Error));

    /// <summary>按单条操作发出通知, 后台调用会切换到界面线程.</summary>
    private static void Show(ToastNotification notification)
    {
        if (Application.Current is not App application)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.BeginInvoke(() => Show(notification));
            return;
        }

        App.Logger.Information("Notification", $"显示 {notification.Level} 通知: {notification.Message}");
        if (application.MainWindow is MainWindow { IsVisible: true } window)
        {
            window.ShowToast(notification);
        }
    }
}
