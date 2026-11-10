using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>保存版本来源、连接数与速度上限的下载设置页面.</summary>
public partial class DownloadSettingsPage
{
    private bool _initializing = true;
    private bool _zeroThreadEasterEggShown;

    /// <summary>读取当前设置并初始化下拉列表.</summary>
    public DownloadSettingsPage()
    {
        InitializeComponent();
        RefreshControls();
    }

    /// <summary>恢复全部下载设置, 保留其他启动器配置.</summary>
    internal void ResetToDefaults()
    {
        if (App.UpdateSettings(App.Settings with
            {
                VersionListSource = VersionListSource.OfficialFirst,
                FileDownloadSource = VersionListSource.OfficialFirst,
                DownloadMaxConnections = 8,
                DownloadSpeedLimitMiBPerSecond = 0
            }))
        {
            App.Logger.Information("Settings", "下载设置已重置为默认值.");
        }

        RefreshControls();
    }

    private void RefreshControls()
    {
        _initializing = true;
        _zeroThreadEasterEggShown |= App.Settings.ZeroDownloadThreadsEggShown;
        ThreadCountSlider.Minimum = _zeroThreadEasterEggShown ? 1 : 0;
        VersionListSourceBox.SelectedIndex = App.Settings.VersionListSource == VersionListSource.MirrorFirst ? 1 : 0;
        FileDownloadSourceBox.SelectedIndex = App.Settings.FileDownloadSource == VersionListSource.MirrorFirst ? 1 : 0;
        ThreadCountSlider.Value = App.Settings.DownloadMaxConnections;
        ThreadCountText.Text = App.Settings.DownloadMaxConnections.ToString(CultureInfo.InvariantCulture);
        SpeedLimitBox.Text = App.Settings.DownloadSpeedLimitMiBPerSecond.ToString("0.##", CultureInfo.InvariantCulture);
        _initializing = false;
    }

    private void FileDownloadSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || FileDownloadSourceBox.SelectedIndex < 0) return;
        VersionListSource selected = FileDownloadSourceBox.SelectedIndex == 1 ? VersionListSource.MirrorFirst : VersionListSource.OfficialFirst;
        if (App.Settings.FileDownloadSource == selected) return;
        if (!App.UpdateSettings(App.Settings with { FileDownloadSource = selected })) RefreshControls();
    }

    private void VersionListSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || VersionListSourceBox.SelectedIndex < 0)
        {
            return;
        }

        VersionListSource selected = VersionListSourceBox.SelectedIndex == 1 ? VersionListSource.MirrorFirst : VersionListSource.OfficialFirst;
        if (App.Settings.VersionListSource == selected)
        {
            return;
        }

        if (App.UpdateSettings(App.Settings with { VersionListSource = selected }))
        {
            App.Logger.Information("Settings", $"版本列表首选来源: {selected}.");
            return;
        }

        RefreshControls();
    }

    private void DownloadValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
        }
    }

    private void ThreadCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThreadCountText is not null)
        {
            ThreadCountText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture);
        }

        if (_initializing || _zeroThreadEasterEggShown || e.NewValue > 0) return;

        // 先锁定状态并修正控件, 避免值变更和失去鼠标捕获时重复触发.
        _zeroThreadEasterEggShown = true;
        _initializing = true;
        ThreadCountSlider.Minimum = 1;
        ThreadCountSlider.Value = 1;
        if (ThreadCountSlider.IsMouseCaptureWithin) Mouse.Capture(null);
        _initializing = false;
        App.UpdateSettings(App.Settings with
        {
            DownloadMaxConnections = 1,
            ZeroDownloadThreadsEggShown = true
        });
        App.Logger.Information("Settings", "触发零下载线程彩蛋, 最大连接数恢复为 1.");
        LauncherDialogs.Information(LocalizationManager.Get("downloadSettings.zeroThreadsTitle"),
            LocalizationManager.Get("downloadSettings.zeroThreadsMessage"));
    }

    private void ThreadCountSlider_Commit(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        int selected = Math.Max(1, (int)Math.Round(ThreadCountSlider.Value));
        if (selected != App.Settings.DownloadMaxConnections)
        {
            if (!App.UpdateSettings(App.Settings with { DownloadMaxConnections = selected }))
            {
                RefreshControls();
            }
        }
    }

    private void ThreadCountSlider_KeyUp(object sender, KeyEventArgs e) => ThreadCountSlider_Commit(sender, e);

    private void DownloadValueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        if (ReferenceEquals(sender, SpeedLimitBox) && (double.TryParse(SpeedLimitBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double speed) || double.TryParse(SpeedLimitBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out speed)))
        {
            App.UpdateSettings(App.Settings with { DownloadSpeedLimitMiBPerSecond = speed });
        }

        RefreshControls();
    }
}
