using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>提供窗口不透明度/明暗模式和主题色设置.</summary>
public partial class PersonalizationPage
{
    private readonly DispatcherTimer _opacitySaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };

    private bool _initializing = true;
    private bool _draggingOpacity;

    /// <summary>从当前配置初始化个性化控件.</summary>
    public PersonalizationPage()
    {
        InitializeComponent();
        _opacitySaveTimer.Tick += (_, _) => CommitOpacity();
        OpacitySlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OpacitySlider_DragStarted));
        OpacitySlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OpacitySlider_DragCompleted));
        AccentPicker.ColorSelected += AccentPicker_ColorSelected;
        RefreshControls();
    }

    /// <summary>恢复全部个性化设置并立即更新页面和窗口.</summary>
    internal void ResetToDefaults()
    {
        _opacitySaveTimer.Stop();
        _draggingOpacity = false;
        if (App.UpdateSettings(new LauncherSettings()))
        {
            App.Logger.Information("Settings", "个性化设置已重置为默认值.");
        }

        RefreshControls();
    }

    private void RefreshControls()
    {
        _opacitySaveTimer.Stop();
        _initializing = true;
        OpacitySlider.Value = App.Settings.WindowOpacity * 100;
        OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        DayThemeButton.IsChecked = App.Settings.Theme == LauncherTheme.Day;
        NightThemeButton.IsChecked = App.Settings.Theme == LauncherTheme.Night;
        AccentPicker.SetColor(App.Settings.AccentColor);
        _initializing = false;
        App.PreviewWindowOpacity(App.Settings.WindowOpacity);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || OpacityValueText is null)
        {
            return;
        }

        OpacityValueText.Text = $"{e.NewValue:0}%";
        App.PreviewWindowOpacity(e.NewValue / 100);
        if (!_draggingOpacity)
        {
            _opacitySaveTimer.Stop();
            _opacitySaveTimer.Start();
        }
    }

    private void OpacitySlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _opacitySaveTimer.Stop();
        _draggingOpacity = true;
    }

    private void OpacitySlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _draggingOpacity = false;
        CommitOpacity();
    }

    private void PersonalizationPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _draggingOpacity = false;
        CommitOpacity();
    }

    private void CommitOpacity()
    {
        _opacitySaveTimer.Stop();
        double opacity = OpacitySlider.Value / 100;
        if (Math.Abs(opacity - App.Settings.WindowOpacity) < 0.001)
        {
            return;
        }

        if (!App.UpdateSettings(App.Settings with { WindowOpacity = opacity }))
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

        CommitOpacity();
        LauncherTheme theme = button == DayThemeButton ? LauncherTheme.Day : LauncherTheme.Night;
        if (!App.UpdateSettings(App.Settings with { Theme = theme }))
        {
            RefreshControls();
        }
    }

    private void AccentPicker_ColorSelected(string color)
    {
        CommitOpacity();
        if (!string.Equals(color, App.Settings.AccentColor, StringComparison.OrdinalIgnoreCase))
        {
            App.UpdateSettings(App.Settings with { AccentColor = color });
        }

        AccentPicker.SetColor(App.Settings.AccentColor);
    }
}
