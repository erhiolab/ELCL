using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ErhioLab.Craft.Launcher.Controls;

public static class ComboBoxAssist
{
    public static readonly DependencyProperty SmoothDropDownProperty = DependencyProperty.RegisterAttached("SmoothDropDown", typeof(bool), typeof(ComboBoxAssist), new PropertyMetadata(false, SmoothDropDownChanged));

    private static readonly DependencyProperty PlaybackOwnerProperty = DependencyProperty.RegisterAttached("PlaybackOwner", typeof(MainWindow), typeof(ComboBoxAssist));

    public static bool GetSmoothDropDown(DependencyObject element) => (bool)element.GetValue(SmoothDropDownProperty);
    public static void SetSmoothDropDown(DependencyObject element, bool value) => element.SetValue(SmoothDropDownProperty, value);

    private static void SmoothDropDownChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ComboBox comboBox) return;
        if ((bool)args.NewValue)
        {
            comboBox.DropDownOpened += DropDownOpened;
            comboBox.DropDownClosed += DropDownClosed;
            comboBox.Unloaded += Unloaded;
            comboBox.PreviewMouseWheel += PreviewMouseWheel;
            if (comboBox.IsDropDownOpen) DropDownOpened(comboBox, EventArgs.Empty);
        }
        else
        {
            comboBox.DropDownOpened -= DropDownOpened;
            comboBox.DropDownClosed -= DropDownClosed;
            comboBox.Unloaded -= Unloaded;
            comboBox.PreviewMouseWheel -= PreviewMouseWheel;
            ReleasePlayback(comboBox);
        }
    }

    private static void DropDownOpened(object? sender, EventArgs args)
    {
        if (sender is not ComboBox comboBox) return;
        if (Window.GetWindow(comboBox) is MainWindow owner)
        {
            comboBox.SetValue(PlaybackOwnerProperty, owner);
            owner.SetBackgroundDropDownOpen(comboBox, true);
        }

        if (SystemParameters.ClientAreaAnimation && GetPopup(comboBox)?.Child is FrameworkElement surface)
        {
            surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
    }

    private static void DropDownClosed(object? sender, EventArgs args)
    {
        if (sender is ComboBox { IsDropDownOpen: false } comboBox) ReleasePlayback(comboBox);
    }

    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        if (sender is ComboBox comboBox) ReleasePlayback(comboBox);
    }

    private static void ReleasePlayback(ComboBox comboBox)
    {
        if (comboBox.GetValue(PlaybackOwnerProperty) is not MainWindow owner) return;
        comboBox.ClearValue(PlaybackOwnerProperty);
        owner.SetBackgroundDropDownOpen(comboBox, false);
    }

    private static Popup? GetPopup(ComboBox comboBox) => comboBox.Template?.FindName("PART_Popup", comboBox) as Popup;

    private static void PreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (sender is not ComboBox { IsDropDownOpen: true } comboBox || args.Delta == 0
                                                                     || Window.GetWindow(comboBox) is not { } window) return;
        HandleOpenDropDownWheel(comboBox, window, args.GetPosition(window), args);
    }

    private static void HandleOpenDropDownWheel(ComboBox comboBox, Window window, Point position, MouseWheelEventArgs args)
    {
        if (GetPopup(comboBox)?.Child is not FrameworkElement surface || PresentationSource.FromVisual(surface) is null) return;
        // 展开时 ComboBox 捕获鼠标. 用屏幕坐标区分弹层和主窗口, 而不是依赖事件来源.
        var screenPoint = window.PointToScreen(position);
        if (new Rect(surface.RenderSize).Contains(surface.PointFromScreen(screenPoint))) return;

        var hit = window.InputHitTest(position) as DependencyObject;
        args.Handled = true;
        comboBox.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
        if (hit is not null)
        {
            // 先让关闭弹层产生的焦点回迁和布局完成, 否则它们可能把滚动位置拉回去.
            _ = window.Dispatcher.InvokeAsync(() => ScrollViewerAssist.ForwardMouseWheel(hit, args), DispatcherPriority.Background);
        }
    }
}
