using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>让嵌套列表在滚动边界将鼠标滚轮交给仍可滚动的外层页面.</summary>
public static class ScrollViewerAssist
{
    public static readonly DependencyProperty ChainMouseWheelProperty = DependencyProperty.RegisterAttached("ChainMouseWheel", typeof(bool), typeof(ScrollViewerAssist), new PropertyMetadata(false, ChainMouseWheelChanged));

    public static bool GetChainMouseWheel(DependencyObject element) => (bool)element.GetValue(ChainMouseWheelProperty);
    public static void SetChainMouseWheel(DependencyObject element, bool value) => element.SetValue(ChainMouseWheelProperty, value);

    private static void ChainMouseWheelChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not UIElement control) return;
        control.PreviewMouseWheel -= PreviewMouseWheel;
        if ((bool)args.NewValue) control.PreviewMouseWheel += PreviewMouseWheel;
    }

    private static void PreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || args.Delta == 0 || sender is not DependencyObject host) return;
        ScrollViewer? inner = args.OriginalSource is DependencyObject source ? AncestorViewer(source, includeSelf: true) : null;
        if (inner is null || !Within(inner, host)) inner = DescendantViewer(host);
        if (inner is null || CanScroll(inner, args.Delta)) return;

        ForwardMouseWheel(inner, args, includeSelf: false);
    }

    internal static void ForwardMouseWheel(DependencyObject source, MouseWheelEventArgs args, bool includeSelf = true)
    {
        var viewer = AncestorViewer(source, includeSelf);
        while (viewer is not null && !CanScroll(viewer, args.Delta)) viewer = AncestorViewer(viewer, includeSelf: false);
        if (viewer is null) return;
        args.Handled = true;
        viewer.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = viewer
        });
    }

    private static bool CanScroll(ScrollViewer viewer, int delta) => viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                                                                     && (delta > 0 ? viewer.VerticalOffset > 0.001 : viewer.VerticalOffset < viewer.ScrollableHeight - 0.001);

    private static ScrollViewer? AncestorViewer(DependencyObject element, bool includeSelf)
    {
        for (DependencyObject? current = includeSelf ? element : Parent(element); current is not null; current = Parent(current))
            if (current is ScrollViewer viewer)
                return viewer;
        return null;
    }

    private static bool Within(DependencyObject element, DependencyObject host)
    {
        for (DependencyObject? current = element; current is not null; current = Parent(current))
            if (ReferenceEquals(current, host))
                return true;
        return false;
    }

    private static ScrollViewer? DescendantViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        if (element is not Visual && element is not System.Windows.Media.Media3D.Visual3D) return null;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (DescendantViewer(VisualTreeHelper.GetChild(element, index)) is { } child)
                return child;
        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) => element is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(element)
        : LogicalTreeHelper.GetParent(element);
}
