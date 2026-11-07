using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>平滑显示三段占比, 只重绘色条, 不在动画期间反复触发页面布局.</summary>
public sealed class AnimatedSegmentBar : FrameworkElement
{
    public static readonly DependencyProperty FirstBrushProperty = RegisterBrush(nameof(FirstBrush));
    public static readonly DependencyProperty SecondBrushProperty = RegisterBrush(nameof(SecondBrush));
    public static readonly DependencyProperty ThirdBrushProperty = RegisterBrush(nameof(ThirdBrush));

    private static readonly DependencyProperty FirstBoundaryProperty = DependencyProperty.Register(
        nameof(FirstBoundary), typeof(double), typeof(AnimatedSegmentBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, (element, _) => ((AnimatedSegmentBar)element).FirstBoundaryChanged?.Invoke(element, EventArgs.Empty)));

    private static readonly DependencyProperty SecondBoundaryProperty = DependencyProperty.Register(
        nameof(SecondBoundary), typeof(double), typeof(AnimatedSegmentBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _hasValues;
    private double _targetFirst;
    private double _targetSecond;
    private int _transitionVersion;

    /// <summary>第一段在当前动画帧中的右边缘位置, 单位为 WPF 逻辑像素.</summary>
    public double FirstBoundaryOffset => Math.Clamp(FirstBoundary, 0, 1) * ActualWidth;

    /// <summary>动画或控件宽度变化时通知跟随分界线的标注.</summary>
    public event EventHandler? FirstBoundaryChanged;

    public AnimatedSegmentBar()
    {
        SnapsToDevicePixels = true;
        Unloaded += (_, _) => StopTransition();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) StopTransition();
        };
    }

    public Brush? FirstBrush
    {
        get => (Brush?)GetValue(FirstBrushProperty);
        set => SetValue(FirstBrushProperty, value);
    }

    public Brush? SecondBrush
    {
        get => (Brush?)GetValue(SecondBrushProperty);
        set => SetValue(SecondBrushProperty, value);
    }

    public Brush? ThirdBrush
    {
        get => (Brush?)GetValue(ThirdBrushProperty);
        set => SetValue(ThirdBrushProperty, value);
    }

    private double FirstBoundary => (double)GetValue(FirstBoundaryProperty);
    private double SecondBoundary => (double)GetValue(SecondBoundaryProperty);

    public void SetValues(double first, double second, double third, TimeSpan transition)
    {
        first = Sanitize(first);
        second = Sanitize(second);
        third = Sanitize(third);
        double total = first + second + third;
        double targetFirst = total > 0 && double.IsFinite(total) ? first / total : 0;
        double targetSecond = total > 0 && double.IsFinite(total) ? (first + second) / total : 0;
        if (_hasValues && targetFirst == _targetFirst && targetSecond == _targetSecond) return;

        // 新数据到来时从屏幕上的当前位置继续, 避免中断上一段动画后跳回旧目标.
        double fromFirst = FirstBoundary;
        double fromSecond = SecondBoundary;
        bool animate = _hasValues && IsLoaded && IsVisible && transition > TimeSpan.Zero;
        _targetFirst = targetFirst;
        _targetSecond = targetSecond;
        int version = ++_transitionVersion;
        BeginAnimation(FirstBoundaryProperty, null);
        BeginAnimation(SecondBoundaryProperty, null);
        SetValue(FirstBoundaryProperty, animate ? fromFirst : targetFirst);
        SetValue(SecondBoundaryProperty, animate ? fromSecond : targetSecond);
        _hasValues = true;
        if (!animate) return;

        SineEase easing = new() { EasingMode = EasingMode.EaseInOut };
        AnimateBoundary(FirstBoundaryProperty, fromFirst, targetFirst, transition, easing, version);
        AnimateBoundary(SecondBoundaryProperty, fromSecond, targetSecond, transition, easing, version);
    }

    private void AnimateBoundary(DependencyProperty property, double from, double to, TimeSpan duration,
        IEasingFunction easing, int version)
    {
        DoubleAnimation animation = new(from, to, duration)
        {
            EasingFunction = easing
        };
        animation.Completed += (_, _) =>
        {
            if (version != _transitionVersion) return;
            SetValue(property, to);
            BeginAnimation(property, null);
        };
        BeginAnimation(property, animation);
    }

    private void StopTransition()
    {
        _transitionVersion++;
        SetValue(FirstBoundaryProperty, _targetFirst);
        SetValue(SecondBoundaryProperty, _targetSecond);
        BeginAnimation(FirstBoundaryProperty, null);
        BeginAnimation(SecondBoundaryProperty, null);
        _hasValues = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double firstFraction = Math.Clamp(FirstBoundary, 0, 1);
        double first = firstFraction * ActualWidth;
        double second = Math.Clamp(SecondBoundary, firstFraction, 1) * ActualWidth;
        drawingContext.DrawRectangle(FirstBrush, null, new Rect(0, 0, first, ActualHeight));
        drawingContext.DrawRectangle(SecondBrush, null, new Rect(first, 0, second - first, ActualHeight));
        drawingContext.DrawRectangle(ThirdBrush, null, new Rect(second, 0, ActualWidth - second, ActualHeight));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        FirstBoundaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double Sanitize(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(AnimatedSegmentBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
