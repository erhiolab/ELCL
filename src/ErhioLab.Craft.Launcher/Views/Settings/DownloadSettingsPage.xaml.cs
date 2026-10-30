using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>保存版本列表首选来源的下载设置页面.</summary>
public partial class DownloadSettingsPage
{
    private bool _initializing = true;

    /// <summary>读取当前设置并初始化下拉列表.</summary>
    public DownloadSettingsPage()
    {
        InitializeComponent();
        RefreshControls();
    }

    /// <summary>只恢复下载源设置, 保留其他启动器配置.</summary>
    internal void ResetToDefaults()
    {
        if (App.UpdateSettings(App.Settings with { VersionListSource = VersionListSource.OfficialFirst }))
        {
            App.Logger.Information("Settings", "下载设置已重置为默认值.");
        }

        RefreshControls();
    }

    private void RefreshControls()
    {
        _initializing = true;
        VersionListSourceBox.SelectedIndex = App.Settings.VersionListSource == VersionListSource.MirrorFirst ? 1 : 0;
        _initializing = false;
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
}
