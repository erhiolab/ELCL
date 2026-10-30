using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Dialogs;

/// <summary>主窗口内复用的自绘弹窗内容和缓动动画.</summary>
public partial class LauncherDialogView : UserControl
{
    private bool _closing;
    private bool _requiresConfirmation;

    /// <summary>用户关闭弹窗后传回是否确认.</summary>
    internal event Action<bool>? Completed;

    /// <summary>初始化弹窗视图.</summary>
    public LauncherDialogView() => InitializeComponent();

    /// <summary>主窗口尚未创建时隐藏周围的遮罩.</summary>
    internal void UseStandalonePresentation() => Backdrop.Visibility = Visibility.Collapsed;

    /// <summary>填充本次提示的文字/按钮和等级颜色.</summary>
    internal void Configure(string title, string message, LauncherDialogLevel level,
        bool requiresConfirmation, string confirmText)
    {
        _closing = false;
        _requiresConfirmation = requiresConfirmation;
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.IsDefault = !requiresConfirmation;
        CancelButton.Visibility = requiresConfirmation ? Visibility.Visible : Visibility.Collapsed;

        bool day = App.Settings.Theme == LauncherTheme.Day;
        Brush levelBrush = level switch
        {
            LauncherDialogLevel.Warning or LauncherDialogLevel.Error or LauncherDialogLevel.Danger =>
                new SolidColorBrush(day
                    ? Color.FromRgb(181, 45, 54)
                    : Color.FromRgb(240, 108, 116)),
            _ => (Brush)FindResource("AccentTextBrush")
        };
        LevelTint.Background = new SolidColorBrush(level == LauncherDialogLevel.Information
            ? Color.FromArgb(51, 44, 139, 207)
            : Color.FromArgb(85, 227, 92, 106));
        DialogTitle.Foreground = levelBrush;
        TitleRule.Background = levelBrush;
        ConfirmButton.BorderBrush = levelBrush;
        ConfirmButton.Foreground = levelBrush;
    }

    /// <summary>立即显示遮罩并将卡片缓动到最终位置.</summary>
    internal void Open()
    {
        Backdrop.BeginAnimation(OpacityProperty, null);
        DialogCard.BeginAnimation(OpacityProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardOffset.BeginAnimation(TranslateTransform.YProperty, null);

        Backdrop.Opacity = 1;
        DialogCard.Opacity = 1;
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;
        CardOffset.Y = 0;
        Animate(Backdrop, OpacityProperty, 0, 1, 160);
        Animate(DialogCard, OpacityProperty, 0, 1, 180);
        Animate(CardScale, ScaleTransform.ScaleXProperty, 0.96, 1, 230);
        Animate(CardScale, ScaleTransform.ScaleYProperty, 0.96, 1, 230);
        Animate(CardOffset, TranslateTransform.YProperty, 12, 0, 230);

        Dispatcher.BeginInvoke(() =>
        {
            if (_requiresConfirmation)
            {
                CancelButton.Focus();
            }
            else
            {
                ConfirmButton.Focus();
            }
        });
    }

    private void Close(bool confirmed)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        double backdropOpacity = Backdrop.Opacity;
        double cardOpacity = DialogCard.Opacity;
        Backdrop.BeginAnimation(OpacityProperty, null);
        DialogCard.BeginAnimation(OpacityProperty, null);
        Backdrop.Opacity = 0;
        DialogCard.Opacity = 0;
        Animate(Backdrop, OpacityProperty, backdropOpacity, 0, 140);

        DoubleAnimation closeAnimation = CreateAnimation(cardOpacity, 0, 140);
        closeAnimation.Completed += (_, _) => Completed?.Invoke(confirmed);
        DialogCard.BeginAnimation(OpacityProperty, closeAnimation);
    }

    private static void Animate(Animatable target, DependencyProperty property,
        double from, double to, int milliseconds) =>
        target.BeginAnimation(property, CreateAnimation(from, to, milliseconds));

    private static void Animate(UIElement target, DependencyProperty property,
        double from, double to, int milliseconds) =>
        target.BeginAnimation(property, CreateAnimation(from, to, milliseconds));

    private static DoubleAnimation CreateAnimation(double from, double to, int milliseconds) => new()
    {
        From = from,
        To = to,
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.Stop
    };

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => Close(true);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close(false);

    private void DialogView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close(false);
    }
}
