using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Dialogs;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>提供不会修改配置的弹窗调试入口.</summary>
public partial class OtherSettingsPage : UserControl
{
    /// <summary>初始化设置中的其他页面.</summary>
    public OtherSettingsPage() => InitializeComponent();

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
}
