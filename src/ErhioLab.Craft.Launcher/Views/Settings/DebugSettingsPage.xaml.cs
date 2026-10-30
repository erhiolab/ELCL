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
        LauncherDialogs.Information("普通提示", "这是普通提示弹窗的调试预览.");
    }

    private void WarningButton_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Debug", "预览警告弹窗.");
        LauncherDialogs.Warning("警告", "这项操作存在风险, 请先检查相关设置.");
    }

    private void ErrorButton_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Debug", "预览错误弹窗.");
        LauncherDialogs.Error("操作失败", "这是操作失败时显示的错误弹窗预览.");
    }

    private async void Second_Confirmation_Button_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Debug", "预览二次确认弹窗.");
        bool confirmed = await LauncherDialogs.ConfirmAsync(
            "二次确认(普通)",
            "这是普通确认弹窗的调试预览. 确认后不会执行任何操作.",
            LauncherDialogLevel.Information);
        App.Logger.Information("Debug", $"普通确认弹窗调试结果: {(confirmed ? "确认" : "取消")}.");
    }

    private void ToastInformationButton_Click(object sender, RoutedEventArgs e) =>
        LauncherToasts.Information("正在检查游戏文件...");

    private void ToastSuccessButton_Click(object sender, RoutedEventArgs e) =>
        LauncherToasts.Success("检查完成, 游戏文件已就绪.");

    private void ToastErrorButton_Click(object sender, RoutedEventArgs e) =>
        LauncherToasts.Error("检查失败, 请查看日志.");

    private void ToastStressButton_Click(object sender, RoutedEventArgs e)
    {
        if (_toastStressTimer.IsEnabled)
        {
            return;
        }

        _toastStressIndex = 0;
        ToastStressButton.IsEnabled = false;
        ToastStressButton.Content = $"测试中 (0/{StressToastCount})";
        App.Logger.Information("Debug", $"开始逐条触发 {StressToastCount} 条随机通知.");
        _toastStressTimer.Start();
    }

    private void ToastStressTimer_Tick(object? sender, EventArgs e)
    {
        int number = ++_toastStressIndex;
        ToastLevel level = (ToastLevel)Random.Shared.Next(3);
        string message = $"性能测试 {number:0000}/{StressToastCount} - {level switch
        {
            ToastLevel.Success => "成功",
            ToastLevel.Error => "错误",
            _ => "信息"
        }}";
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
            ToastStressButton.Content = $"测试中 ({number}/{StressToastCount})";
        }

        if (number < StressToastCount)
        {
            return;
        }

        _toastStressTimer.Stop();
        ToastStressButton.Content = $"性能测试 ({StressToastCount} 条)";
        ToastStressButton.IsEnabled = true;
        App.Logger.Information("Debug", $"已逐条触发 {StressToastCount} 条随机通知.");
    }
}
