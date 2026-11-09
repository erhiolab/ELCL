using System.Windows;
using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>统一展示数值与其说明, 使用共享样式适应主题和窄窗口.</summary>
public class MetricReadout : Control
{
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(MetricReadout), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(MetricReadout), new PropertyMetadata(string.Empty));

    static MetricReadout() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(MetricReadout), new FrameworkPropertyMetadata(typeof(MetricReadout)));

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
