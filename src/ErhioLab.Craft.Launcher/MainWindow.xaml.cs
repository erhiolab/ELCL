using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Views.Launch;
using ErhioLab.Craft.Launcher.Views.Shared;

namespace ErhioLab.Craft.Launcher;

/// <summary>承载自绘窗口/顶部导航及当前页面的侧栏和主体.</summary>
public partial class MainWindow : Window
{
    // GridLength 不能直接使用 DoubleAnimation, 因此用依赖属性驱动侧栏列宽.
    private static readonly DependencyProperty SidebarWidthProperty = DependencyProperty.Register(
        nameof(SidebarWidth),
        typeof(double),
        typeof(MainWindow),
        new PropertyMetadata(310.0, OnSidebarWidthChanged));

    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmncrpDisabled = 1;
    private const int DwmwaBorderColor = 34;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HitTestCaption = 2;
    private const uint MonitorDefaultToNearest = 2;
    private const double MinSidebarWidth = 135;
    private readonly IReadOnlyDictionary<ShellSection, ShellRoute> _routes;
    private bool _hasNavigated;
    private bool _navigationIndicatorReady;
    private int _sidebarAnimationVersion;
    private RadioButton? _selectedNavigationButton;
    private Point? _maximizedDragStart;

    private double SidebarWidth
    {
        get => (double)GetValue(SidebarWidthProperty);
        set => SetValue(SidebarWidthProperty, value);
    }

    /// <summary>初始化窗口并打开启动页.</summary>
    public MainWindow()
    {
        InitializeComponent();

        _routes = CreateRoutes();
        LaunchNavigation.IsChecked = true;
    }

    private static IReadOnlyDictionary<ShellSection, ShellRoute> CreateRoutes()
    {
        return new Dictionary<ShellSection, ShellRoute>
        {
            [ShellSection.Launch] = new(310, new LaunchSidebarView(), new LaunchPage()),
            [ShellSection.Download] = new(
                0,
                new SectionSidebarView("游戏版本", "Mod 与资源", "整合包"),
                new PlaceholderPage(
                    "02 / DOWNLOAD", "下载", "游戏版本/Mod/资源包与整合包会在这里提供. ")),
            [ShellSection.Settings] = new(
                0,
                new SectionSidebarView("启动器", "游戏", "网络与下载"),
                new PlaceholderPage(
                    "03 / SETTINGS", "设置", "启动器外观/Java/内存和下载选项会在这里提供. ")),
            [ShellSection.More] = new(
                0,
                new SectionSidebarView("帮助", "关于 ELCL"),
                new PlaceholderPage(
                    "04 / MORE", "更多", "帮助文档/反馈入口和关于信息会在这里提供. "))
        };
    }

    private void PrimaryNavigation_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string sectionName }
            || !Enum.TryParse(sectionName, out ShellSection section)
            || !_routes.TryGetValue(section, out ShellRoute? route))
        {
            return;
        }

        SidebarHost.Content = route.Sidebar;
        PageHost.Content = route.Page;
        _selectedNavigationButton = (RadioButton)sender;

        if (_navigationIndicatorReady)
        {
            MoveNavigationIndicator(_selectedNavigationButton, true);
        }

        if (!_hasNavigated)
        {
            SetInitialSidebarWidth(route);
            _hasNavigated = true;
            return;
        }

        AnimateSidebarWidth(route);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _navigationIndicatorReady = true;
        MoveNavigationIndicator(_selectedNavigationButton ?? LaunchNavigation, false);
    }

    private void TopNavigationHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_navigationIndicatorReady && _selectedNavigationButton is not null)
        {
            MoveNavigationIndicator(_selectedNavigationButton, false);
        }
    }

    private void MoveNavigationIndicator(RadioButton button, bool animate)
    {
        double targetX = button.TransformToAncestor(TopNavigationHost)
            .Transform(new Point(button.ActualWidth / 2, 0)).X - NavigationIndicator.Width / 2;

        if (!animate)
        {
            NavigationIndicatorOffset.BeginAnimation(TranslateTransform.XProperty, null);
            NavigationIndicatorOffset.X = targetX;
            NavigationIndicator.Opacity = 1;
            return;
        }

        double currentX = NavigationIndicatorOffset.X;
        NavigationIndicatorOffset.BeginAnimation(TranslateTransform.XProperty, null);
        NavigationIndicatorOffset.X = targetX;
        NavigationIndicatorOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = currentX,
            To = targetX,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private void SetInitialSidebarWidth(ShellRoute route)
    {
        if (route.SidebarWidth == 0)
        {
            SidebarColumn.Width = GridLength.Auto;
            return;
        }

        SidebarWidth = Math.Max(MinSidebarWidth, route.SidebarWidth);
    }

    private void AnimateSidebarWidth(ShellRoute route)
    {
        double currentWidth = SidebarColumn.ActualWidth;
        bool autoWidth = route.SidebarWidth == 0;
        double targetWidth = Math.Max(
            MinSidebarWidth,
            autoWidth ? MeasureSidebarWidth(route.Sidebar) : route.SidebarWidth);
        int animationVersion = ++_sidebarAnimationVersion;
        BeginAnimation(SidebarWidthProperty, null);
        if (Math.Abs(currentWidth - targetWidth) < 0.5)
        {
            SidebarWidth = targetWidth;
            SidebarColumn.Width = autoWidth ? GridLength.Auto : new GridLength(targetWidth);
            return;
        }

        // 自动宽度先按内容测量并过渡, 动画结束后恢复 Auto 以响应字体和内容变化.
        SidebarWidth = targetWidth;
        DoubleAnimation animation = new()
        {
            From = currentWidth,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 },
            FillBehavior = FillBehavior.Stop
        };
        if (autoWidth)
        {
            animation.Completed += (_, _) =>
            {
                if (animationVersion == _sidebarAnimationVersion)
                {
                    SidebarColumn.Width = GridLength.Auto;
                }
            };
        }

        BeginAnimation(SidebarWidthProperty, animation);
    }

    private double MeasureSidebarWidth(FrameworkElement sidebar)
    {
        sidebar.Measure(new Size(double.PositiveInfinity, Math.Max(0, ActualHeight - 54)));
        return sidebar.DesiredSize.Width + 1;
    }

    private static void OnSidebarWidthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is MainWindow window && window.SidebarColumn is not null)
        {
            window.SidebarColumn.Width = new GridLength((double)e.NewValue);
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessageHook);
        int renderingPolicy = DwmncrpDisabled;
        int borderColor = DwmColorNone;

        _ = DwmSetWindowAttribute(handle, DwmwaNcRenderingPolicy, ref renderingPolicy, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
    }

    private static IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        IntPtr monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitorHandle, ref monitor))
        {
            return IntPtr.Zero;
        }

        // 工作区已排除任务栏, 任务栏在屏幕任意边缘时都应留出空间.
        MinMaxInfo bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition = new NativePoint(
            monitor.WorkArea.Left - monitor.MonitorArea.Left,
            monitor.WorkArea.Top - monitor.MonitorArea.Top);
        bounds.MaxSize = new NativePoint(
            monitor.WorkArea.Right - monitor.WorkArea.Left,
            monitor.WorkArea.Bottom - monitor.WorkArea.Top);
        Marshal.StructureToPtr(bounds, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Maximized)
            {
                _maximizedDragStart = e.GetPosition(this);
                TitleBar.CaptureMouse();
                return;
            }

            DragMove();
        }
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_maximizedDragStart is not Point start)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndMaximizedDrag();
            return;
        }

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // 还原前记录鼠标的屏幕位置, 让还原后的标题栏保持在鼠标下方.
        Point screenPoint = PointToScreen(current);
        Point logicalScreenPoint = PresentationSource.FromVisual(this)?.CompositionTarget?
            .TransformFromDevice.Transform(screenPoint) ?? screenPoint;
        double horizontalRatio = Math.Clamp(start.X / ActualWidth, 0, 1);
        double restoredWidth = RestoreBounds.Width;

        EndMaximizedDrag();
        SystemCommands.RestoreWindow(this);
        Left = logicalScreenPoint.X - restoredWidth * horizontalRatio;
        Top = logicalScreenPoint.Y - 20;
        e.Handled = true;

        // 用同一次鼠标按下进入系统拖动循环, 避免还原后必须重新点击.
        _ = ReleaseCapture();
        _ = SendMessage(
            new WindowInteropHelper(this).Handle,
            WmNcLeftButtonDown,
            new IntPtr(HitTestCaption),
            IntPtr.Zero);
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndMaximizedDrag();
    }

    private void EndMaximizedDrag()
    {
        _maximizedDragStart = null;
        if (TitleBar.IsMouseCaptured)
        {
            TitleBar.ReleaseMouseCapture();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    // 以下字段由 Win32 互操作封送填充, 编译器无法识别赋值过程.
#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }
#pragma warning restore CS0649
}
