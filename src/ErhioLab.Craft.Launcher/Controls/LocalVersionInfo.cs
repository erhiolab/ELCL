using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Versions;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>本地版本共用信息行: 图标/名称/描述和元数据标签.</summary>
public class LocalVersionInfo : Control
{
    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(
        nameof(Info), typeof(LocalVersionPresentation), typeof(LocalVersionInfo));

    static LocalVersionInfo() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(LocalVersionInfo), new FrameworkPropertyMetadata(typeof(LocalVersionInfo)));

    public LocalVersionPresentation? Info
    {
        get => (LocalVersionPresentation?)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }
}
