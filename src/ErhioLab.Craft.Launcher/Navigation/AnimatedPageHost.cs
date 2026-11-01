using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>在两个页面之间同时播放淡入和水平位移, 保留页面实例的状态.</summary>
internal sealed class AnimatedPageHost : Grid
{
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(260));

    private readonly ContentControl[] _layers = [CreateLayer(), CreateLayer()];
    private int _activeIndex;
    private int _transitionVersion;
    private bool _transitioning;

    /// <summary>以指定页面作为初始内容.</summary>
    internal AnimatedPageHost(UIElement initialPage)
    {
        ClipToBounds = true;
        _layers[0].Content = initialPage;
        _layers[1].Opacity = 0;
        Children.Add(_layers[0]);
        Children.Add(_layers[1]);
        Unloaded += (_, _) => CompleteTransition(_transitionVersion);
    }

    /// <summary>切换到目标页面; 返回时位移方向相反.</summary>
    internal void Navigate(UIElement page, bool back = false)
    {
        if (_transitioning)
        {
            CompleteTransition(_transitionVersion);
        }

        ContentControl outgoing = _layers[_activeIndex];
        if (ReferenceEquals(outgoing.Content, page))
        {
            return;
        }

        int nextIndex = 1 - _activeIndex;
        ContentControl incoming = _layers[nextIndex];
        TranslateTransform outgoingOffset = (TranslateTransform)outgoing.RenderTransform;
        TranslateTransform incomingOffset = (TranslateTransform)incoming.RenderTransform;
        int version = ++_transitionVersion;
        double direction = back ? -1 : 1;

        incoming.Content = page;
        incoming.Opacity = 1;
        incomingOffset.X = 0;
        outgoing.Opacity = 0;
        outgoingOffset.X = direction * -16;
        outgoing.IsHitTestVisible = false;
        incoming.IsHitTestVisible = false;
        Panel.SetZIndex(incoming, 1);
        Panel.SetZIndex(outgoing, 0);
        _activeIndex = nextIndex;
        _transitioning = true;

        outgoing.BeginAnimation(OpacityProperty, CreateAnimation(1, 0));
        outgoingOffset.BeginAnimation(TranslateTransform.XProperty, CreateAnimation(0, direction * -16));
        incomingOffset.BeginAnimation(TranslateTransform.XProperty, CreateAnimation(direction * 22, 0));
        DoubleAnimation entrance = CreateAnimation(0, 1);
        entrance.Completed += (_, _) => CompleteTransition(version);
        incoming.BeginAnimation(OpacityProperty, entrance);
    }

    private void CompleteTransition(int version)
    {
        if (!_transitioning || version != _transitionVersion)
        {
            return;
        }

        ContentControl outgoing = _layers[1 - _activeIndex];
        ContentControl incoming = _layers[_activeIndex];
        outgoing.BeginAnimation(OpacityProperty, null);
        incoming.BeginAnimation(OpacityProperty, null);
        ((TranslateTransform)outgoing.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
        ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
        outgoing.Content = null;
        outgoing.Opacity = 0;
        ((TranslateTransform)outgoing.RenderTransform).X = 0;
        incoming.Opacity = 1;
        ((TranslateTransform)incoming.RenderTransform).X = 0;
        incoming.IsHitTestVisible = true;
        _transitioning = false;
    }

    private static ContentControl CreateLayer() => new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        RenderTransform = new TranslateTransform()
    };

    private static DoubleAnimation CreateAnimation(double from, double to) => new(from, to, TransitionDuration)
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.Stop
    };
}
