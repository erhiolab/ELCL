using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>提供窗口外观/语言/容量单位/标题栏和社区资源显示设置.</summary>
public partial class PersonalizationPage
{
    private static readonly MemoryUnitSystem[] MemoryUnitChoices = [MemoryUnitSystem.Binary, MemoryUnitSystem.Decimal];
    private readonly DispatcherTimer _opacitySaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };
    private readonly DispatcherTimer _titleTextSaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };

    private bool _initializing = true;
    private bool _draggingOpacity;
    internal event Action? BackgroundManagementRequested;

    /// <summary>从当前配置初始化个性化控件.</summary>
    public PersonalizationPage()
    {
        InitializeComponent();
        _opacitySaveTimer.Tick += (_, _) => CommitOpacity();
        _titleTextSaveTimer.Tick += (_, _) => CommitTitleText();
        OpacitySlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OpacitySlider_DragStarted));
        OpacitySlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OpacitySlider_DragCompleted));
        AccentPicker.ColorSelected += AccentPicker_ColorSelected;
        LanguageComboBox.ItemsSource = LocalizationManager.AvailableLanguages
            .Select(code => new KeyValuePair<string, string>(code, LocalizationManager.GetName(code)))
            .ToArray();
        RefreshControls();
    }

    /// <summary>恢复全部个性化设置并立即更新页面和窗口.</summary>
    internal void ResetToDefaults()
    {
        _opacitySaveTimer.Stop();
        _titleTextSaveTimer.Stop();
        _draggingOpacity = false;
        if (App.UpdateSettings(App.Settings with
            {
                Theme = LauncherTheme.Day,
                Language = LocalizationManager.DefaultLanguage,
                MemoryUnit = MemoryUnitSystem.Binary,
                ModListPresentation = ModListPresentation.NameFirst,
                ModTranslationEnabled = true,
                AccentColor = "#80CEFF",
                WindowOpacity = 1.0,
                Background = new LauncherBackgroundSettings(),
                HiddenFeatures = LauncherHiddenFeatures.Debug,
                TitleBarMode = TitleBarMode.Default,
                TitleBarText = "ELCL"
            })
           )
        {
            App.Logger.Information("Settings", "个性化设置已重置为默认值.");
        }

        RefreshControls();

    }

    private void RefreshControls()
    {
        _opacitySaveTimer.Stop();
        _titleTextSaveTimer.Stop();
        _initializing = true;
        OpacitySlider.Value = App.Settings.WindowOpacity * 100;
        OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        DayThemeButton.IsChecked = App.Settings.Theme == LauncherTheme.Day;
        NightThemeButton.IsChecked = App.Settings.Theme == LauncherTheme.Night;
        AccentPicker.SetColor(App.Settings.AccentColor);
        BackgroundEnabledBox.IsChecked = App.Settings.BackgroundOptions.Enabled;
        LanguageComboBox.SelectedValue = App.Settings.Language;
        MemoryUnitBox.SelectedIndex = Array.IndexOf(MemoryUnitChoices, App.Settings.MemoryUnit);
        ModPresentationBox.SelectedIndex = (int)App.Settings.ModListPresentation;
        RefreshModTranslationControls();
        NoTitleButton.IsChecked = App.Settings.TitleBarMode == TitleBarMode.None;
        DefaultTitleButton.IsChecked = App.Settings.TitleBarMode == TitleBarMode.Default;
        CustomTitleButton.IsChecked = App.Settings.TitleBarMode == TitleBarMode.Text;
        TitleTextBox.Text = App.Settings.TitleBarText ?? string.Empty;
        TitleTextRow.Visibility = App.Settings.TitleBarMode == TitleBarMode.Text ? Visibility.Visible : Visibility.Collapsed;
        _initializing = false;
        App.PreviewWindowOpacity(App.Settings.WindowOpacity);
        App.PreviewTitleBar(App.Settings.TitleBarMode, App.Settings.TitleBarText ?? string.Empty);
    }

    private void BackgroundManagement_Click(object sender, RoutedEventArgs e) => BackgroundManagementRequested?.Invoke();

    private void BackgroundEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var options = App.Settings.BackgroundOptions with { Enabled = BackgroundEnabledBox.IsChecked == true };
        if (options != App.Settings.BackgroundOptions && !App.UpdateSettings(App.Settings with { Background = options })) RefreshControls();
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
        CommitTitleText();
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

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LanguageComboBox.SelectedValue is not string language || language == App.Settings.Language)
            return;
        if (!App.UpdateSettings(App.Settings with { Language = language })) RefreshControls();
        else
        {
            _initializing = true;
            RefreshModTranslationControls();
            _initializing = false;
        }
    }

    private void PersonalizationPage_Loaded(object sender, RoutedEventArgs e) => RefreshControls();

    private void RefreshModTranslationControls()
    {
        ModTranslationBox.IsEnabled = ModLocalization.SupportsLanguage(App.Settings.Language);
        ModTranslationBox.IsChecked = ModLocalization.IsEnabled(App.Settings);
    }

    private void ModTranslation_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || !ModLocalization.SupportsLanguage(App.Settings.Language)) return;
        bool enabled = ModTranslationBox.IsChecked == true;
        if (enabled == App.Settings.ModTranslationEnabled) return;
        App.Logger.Information("Settings", $"开始设置 Mods 汉化: {enabled}.");
        if (!App.UpdateSettings(App.Settings with { ModTranslationEnabled = enabled }))
        {
            RefreshControls();
            return;
        }
        App.Logger.Information("Settings", $"Mods 汉化偏好已保存: {enabled}.");
    }

    private void MemoryUnitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || MemoryUnitBox.SelectedIndex < 0) return;
        var unit = MemoryUnitChoices[MemoryUnitBox.SelectedIndex];
        if (unit == App.Settings.MemoryUnit) return;
        App.Logger.Information("Settings", $"开始切换内存单位: {App.Settings.MemoryUnit} -> {unit}.");
        if (!App.UpdateSettings(App.Settings with { MemoryUnit = unit }))
        {
            RefreshControls();
            return;
        }

        App.Logger.Information("Settings", $"内存单位已保存: {unit}.");
    }

    private void ModPresentationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ModPresentationBox.SelectedIndex < 0) return;
        var selected = (ModListPresentation)ModPresentationBox.SelectedIndex;
        if (selected == App.Settings.ModListPresentation) return;
        if (!App.UpdateSettings(App.Settings with { ModListPresentation = selected }))
        {
            RefreshControls();
            return;
        }
        App.Logger.Information("Settings", $"Mods 列表样式已保存: {selected}.");
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

    private void TitleBarModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing || sender is not RadioButton button)
        {
            return;
        }

        if (!CommitTitleText())
        {
            return;
        }
        TitleBarMode mode = button == NoTitleButton ? TitleBarMode.None : button == CustomTitleButton ? TitleBarMode.Text : TitleBarMode.Default;
        if (!App.UpdateSettings(App.Settings with { TitleBarMode = mode }))
        {
            RefreshControls();
            return;
        }

        TitleTextRow.Visibility = mode == TitleBarMode.Text ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing || CustomTitleButton.IsChecked != true)
        {
            return;
        }

        App.PreviewTitleBar(TitleBarMode.Text, TitleTextBox.Text);
        _titleTextSaveTimer.Stop();
        _titleTextSaveTimer.Start();
    }

    private bool CommitTitleText()
    {
        _titleTextSaveTimer.Stop();
        if (TitleTextBox.Text == App.Settings.TitleBarText)
        {
            return true;
        }

        if (!App.UpdateSettings(App.Settings with { TitleBarText = TitleTextBox.Text }))
        {
            RefreshControls();
            return false;
        }

        return true;
    }
}
