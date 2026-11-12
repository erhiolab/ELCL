using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>统一承载线框 Geometry 的操作按钮, 图标的缩放/描边和布局由共享样式负责.</summary>
public class IconButton : Button
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(IconButton));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
