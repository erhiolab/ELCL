using ErhioLab.Craft.Launcher.Localization;
using System.Windows;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>集中提供弹窗和通知条的调试入口.</summary>
public partial class DebugSettingsPage
{
    private const int StressToastCount = 1000;

    private readonly DispatcherTimer _toastStressTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(25)
    };

    private int _toastStressIndex;

    /// <summary>初始化调试页面.</summary>
    public DebugSettingsPage()
    {
        InitializeComponent();
        _toastStressTimer.Tick += ToastStressTimer_Tick;
    }

    private void InformationButton_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Debug", "预览普通提示弹窗.");
        LauncherDialogs.Information(LocalizationManager.Get("debug.information"), LocalizationManager.Get("debug.informationDialogPreview"));
    }

    private void WarningButton_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Debug", "预览警告弹窗.");
        LauncherDialogs.Warning(LocalizationManager.Get("debug.warning"), LocalizationManager.Get("debug.riskDescription"));
    }

    private void ErrorButton_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Debug", "预览错误弹窗.");
        LauncherDialogs.Error(LocalizationManager.Get("debug.operationFailed"), LocalizationManager.Get("debug.errorDialogPreview"));
    }

    private async void Second_Confirmation_Button_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Debug", "预览二次确认弹窗.");
        bool confirmed = await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("debug.confirmationNormal"), LocalizationManager.Get("debug.confirmationPreview"), LauncherDialogLevel.Information);
        App.Logger.Information("Debug", $"普通确认弹窗调试结果: {(confirmed ? "确认" : "取消")}.");
    }

    private void ToastInformationButton_Click(object sender, RoutedEventArgs e) =>
        LauncherToasts.Information(LocalizationManager.Get("debug.checkingGameFiles"));

    private void ToastSuccessButton_Click(object sender, RoutedEventArgs e) =>
        LauncherToasts.Success(LocalizationManager.Get("debug.checkComplete"));

    private void ToastErrorButton_Click(object sender, RoutedEventArgs e) =>
        LauncherToasts.Error(LocalizationManager.Get("debug.checkFailed"));

    private void ToastStressButton_Click(object sender, RoutedEventArgs e)
    {
        if (_toastStressTimer.IsEnabled)
        {
            return;
        }

        _toastStressIndex = 0;
        ToastStressButton.IsEnabled = false;
        ToastStressButton.Content = string.Format(LocalizationManager.Get("debug.testing"), 0, StressToastCount);
        App.Logger.Information("Debug", $"开始逐条触发 {StressToastCount} 条随机通知.");
        _toastStressTimer.Start();
    }

    private void ToastStressTimer_Tick(object? sender, EventArgs e)
    {
        int number = ++_toastStressIndex;
        ToastLevel level = (ToastLevel)Random.Shared.Next(3);
        string message = string.Format(LocalizationManager.Get("debug.message"), number, StressToastCount, level switch
        {
            ToastLevel.Success => LocalizationManager.Get("debug.success"),
            ToastLevel.Error => LocalizationManager.Get("debug.error"),
            _ => LocalizationManager.Get("debug.informationTitle")
        });
        switch (level)
        {
            case ToastLevel.Success:
                LauncherToasts.Success(message);
                break;
            case ToastLevel.Error:
                LauncherToasts.Error(message);
                break;
            default:
                LauncherToasts.Information(message);
                break;
        }

        if (number % 25 == 0 || number == StressToastCount)
        {
            ToastStressButton.Content = string.Format(LocalizationManager.Get("debug.testing"), number, StressToastCount);
        }

        if (number < StressToastCount)
        {
            return;
        }

        _toastStressTimer.Stop();
        ToastStressButton.Content = string.Format(LocalizationManager.Get("debug.stressButton"), StressToastCount);
        ToastStressButton.IsEnabled = true;
        App.Logger.Information("Debug", $"已逐条触发 {StressToastCount} 条随机通知.");
    }
}
