using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>Shared settings and toolbox card with a header, divider, and content.</summary>
public class LauncherCard : HeaderedContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(LauncherCard));

    /// <summary>可选的标题图标, 与其他卡片共用主题和布局.</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    static LauncherCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LauncherCard),
            new FrameworkPropertyMetadata(typeof(LauncherCard)));
    }
}
