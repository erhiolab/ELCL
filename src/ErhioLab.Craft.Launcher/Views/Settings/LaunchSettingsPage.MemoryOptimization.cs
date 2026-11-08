using System.Windows;

namespace ErhioLab.Craft.Launcher.Views.Settings;

public partial class LaunchSettingsPage
{
    private void RefreshMemoryOptimizationControls()
    {
        OptimizeMemoryButton.IsEnabled = !App.MemoryOptimizer.IsOptimizing;
        OptimizeMemoryButton.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty,
            App.MemoryOptimizer.IsOptimizing ? "I18n.memoryOptimization.busy" : "I18n.memoryOptimization.button");
    }

    private async void OptimizeMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MemoryOptimizer.IsOptimizing) return;
        try
        {
            await App.MemoryOptimizer.OptimizeAsync(showNotifications: true);
        }
        catch (Exception)
        {
            // 服务已经通过通知条和包含异常的日志反馈, 不重复弹窗. 
        }
        finally
        {
            RefreshMemoryUsage(animate: true);
        }
    }

    private void OptimizeMemoryBeforeLaunch_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingLaunchOptions) return;
        bool enabled = OptimizeMemoryBeforeLaunchBox.IsChecked == true;
        if (enabled == App.Settings.OptimizeMemoryBeforeLaunch) return;
        SaveLaunchOptions(App.Settings with { OptimizeMemoryBeforeLaunch = enabled }, $"启动前内存优化已保存: {enabled}.");
    }
}
