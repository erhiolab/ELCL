using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>以高度和透明度缓动展开内容, 收起后移除布局占位与键盘导航.</summary>
public sealed class AnimatedReveal : Decorator
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(AnimatedReveal), new PropertyMetadata(false, ExpansionChanged));

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(AnimatedReveal),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure, (element, args) => ((AnimatedReveal)element).Opacity = (double)args.NewValue));

    private int _animationVersion;

    public AnimatedReveal()
    {
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    private double Progress => (double)GetValue(ProgressProperty);

    private static void ExpansionChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        AnimatedReveal reveal = (AnimatedReveal)element;
        bool expanded = (bool)args.NewValue;
        double from = reveal.Progress;
        int version = ++reveal._animationVersion;
        reveal.BeginAnimation(ProgressProperty, null);
        reveal.SetValue(ProgressProperty, expanded ? 1d : 0d);
        reveal.IsHitTestVisible = expanded;
        KeyboardNavigation.SetTabNavigation(reveal, expanded ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
        if (!reveal.IsLoaded)
        {
            reveal.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        reveal.Visibility = Visibility.Visible;
        DoubleAnimation animation = new(from, expanded ? 1 : 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (version == reveal._animationVersion && !expanded) reveal.Visibility = Visibility.Collapsed;
        };
        reveal.BeginAnimation(ProgressProperty, animation);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return new Size();
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * Progress);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(0, 0, arrangeSize.Width, Child.DesiredSize.Height));
        return arrangeSize;
    }
}
