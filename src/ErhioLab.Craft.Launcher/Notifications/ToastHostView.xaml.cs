using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ErhioLab.Craft.Launcher.Notifications;

/// <summary>在窗口左下角叠放通知条, 每条通知分别计时和退场.</summary>
public partial class ToastHostView
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);
    private static readonly Brush InformationBrush = CreateBrush(46, 155, 232);
    private static readonly Brush SuccessBrush = CreateBrush(41, 168, 106);
    private static readonly Brush ErrorBrush = CreateBrush(218, 65, 78);
    private static readonly Brush TextBrush = CreateBrush(255, 255, 255);

    /// <summary>初始化通知条容器.</summary>
    public ToastHostView() => InitializeComponent();

    /// <summary>独立触发一条通知, 并为它启动专属的到期计时器.</summary>
    internal void Show(ToastNotification notification)
    {
        Border view = CreateView(notification);
        NotificationStack.Children.Add(view);
        AnimateEntrance(view);

        DispatcherTimer expirationTimer = new() { Interval = Lifetime };
        expirationTimer.Tick += (_, _) =>
        {
            expirationTimer.Stop();
            AnimateExit(view);
        };
        expirationTimer.Start();
    }

    private static Border CreateView(ToastNotification notification)
    {
        Brush background = notification.Level switch
        {
            ToastLevel.Success => SuccessBrush,
            ToastLevel.Error => ErrorBrush,
            _ => InformationBrush
        };

        return new Border
        {
            MaxWidth = 350,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 7, 12, 7),
            Background = background,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = new TranslateTransform(),
            Child = new TextBlock
            {
                Text = notification.Message,
                MaxWidth = 326,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static void AnimateEntrance(Border view)
    {
        TranslateTransform offset = (TranslateTransform)view.RenderTransform;
        view.Opacity = 1;
        offset.Y = 0;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
        {
            FillBehavior = FillBehavior.Stop
        });
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0,
            TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private void AnimateExit(Border view)
    {
        DoubleAnimation fade = new(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => NotificationStack.Children.Remove(view);
        view.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)view.RenderTransform).BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(0, -24, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
