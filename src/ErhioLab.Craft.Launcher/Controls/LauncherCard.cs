using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>设置页和百宝箱共用的卡片, 包含标题, 分隔线和内容区.</summary>
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
