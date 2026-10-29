using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>提供窗口不透明度/明暗模式和主题色设置.</summary>
public partial class PersonalizationPage : UserControl
{
    private bool _initializing = true;

    /// <summary>从当前配置初始化个性化控件.</summary>
    public PersonalizationPage()
    {
        InitializeComponent();
        AccentPicker.ColorSelected += AccentPicker_ColorSelected;
        RefreshControls();
    }

    private void RefreshControls()
    {
        _initializing = true;
        OpacitySlider.Value = App.Settings.WindowOpacity * 100;
        OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        DayThemeButton.IsChecked = App.Settings.Theme == LauncherTheme.Day;
        NightThemeButton.IsChecked = App.Settings.Theme == LauncherTheme.Night;
        AccentPicker.SetColor(App.Settings.AccentColor);
        _initializing = false;
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || OpacityValueText is null)
        {
            return;
        }

        OpacityValueText.Text = $"{e.NewValue:0}%";
        if (!App.UpdateSettings(App.Settings with { WindowOpacity = e.NewValue / 100 }))
        {
            RefreshControls();
        }
    }

    private void ThemeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing || sender is not RadioButton button)
        {
            return;
        }

        LauncherTheme theme = button == DayThemeButton ? LauncherTheme.Day : LauncherTheme.Night;
        if (!App.UpdateSettings(App.Settings with { Theme = theme }))
        {
            RefreshControls();
        }
    }

    private void AccentPicker_ColorSelected(string color)
    {
        if (!string.Equals(color, App.Settings.AccentColor, StringComparison.OrdinalIgnoreCase))
        {
            App.UpdateSettings(App.Settings with { AccentColor = color });
        }

        AccentPicker.SetColor(App.Settings.AccentColor);
    }
}
