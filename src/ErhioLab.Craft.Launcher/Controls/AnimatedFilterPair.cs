using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>第二个筛选项出现时, 平滑平分第一个筛选项的横向空间.</summary>
public sealed class AnimatedFilterPair : Panel
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(AnimatedFilterPair), new PropertyMetadata(false, ExpandedChanged));

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double), typeof(AnimatedFilterPair),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public AnimatedFilterPair() => ClipToBounds = true;

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    private double Progress => Math.Clamp((double)GetValue(ProgressProperty), 0, 1);

    private static void ExpandedChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var panel = (AnimatedFilterPair)target;
        double current = panel.Progress;
        panel.BeginAnimation(ProgressProperty, null);
        panel.SetValue(ProgressProperty, panel.IsExpanded ? 1d : 0d);
        if (!panel.IsLoaded) return;
        panel.BeginAnimation(ProgressProperty, new DoubleAnimation(current, panel.IsExpanded ? 1 : 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count != 2) return default;
        double width = double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width;
        double second = Math.Max(0, width - 12) * Progress / 2;
        InternalChildren[0].Measure(new Size(Math.Max(0, width - second - 12 * Progress), availableSize.Height));
        InternalChildren[1].Measure(new Size(Math.Max(0, (width - 12) / 2), availableSize.Height));
        return new Size(width, Math.Max(InternalChildren[0].DesiredSize.Height, InternalChildren[1].DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count != 2) return finalSize;
        double second = Math.Max(0, finalSize.Width - 12) * Progress / 2;
        double first = Math.Max(0, finalSize.Width - second - 12 * Progress);
        InternalChildren[0].Arrange(new Rect(0, 0, first, finalSize.Height));
        var child = InternalChildren[1];
        child.Opacity = Progress;
        child.IsHitTestVisible = IsExpanded;
        KeyboardNavigation.SetTabNavigation(child, IsExpanded ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
        child.Arrange(new Rect(first + 12 * Progress, 0, Math.Max(0, (finalSize.Width - 12) / 2), finalSize.Height));
        return finalSize;
    }
}
