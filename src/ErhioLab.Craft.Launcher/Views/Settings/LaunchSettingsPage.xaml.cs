using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>保存游戏启动预设/预览内存分配并管理本机 Java 列表.</summary>
public partial class LaunchSettingsPage
{
    private const int PreviewCount = 3;
    private static readonly GameProcessPriority[] PriorityChoices = [GameProcessPriority.High, GameProcessPriority.Normal, GameProcessPriority.Low];
    private static readonly GameWindowMode[] WindowChoices = [GameWindowMode.Fullscreen, GameWindowMode.Default, GameWindowMode.MatchLauncher, GameWindowMode.Custom, GameWindowMode.Maximized];
    private bool _showAllJava;
    private int _expansionVersion;
    private bool _initializingLaunchOptions = true;
    private bool _memoryFailureLogged;
    private bool _memoryPercentDirty;
    private readonly HashSet<TextBox> _dirtyWindowSize = [];
    private PhysicalMemorySnapshot? _memorySnapshot;
    private readonly DispatcherTimer _memoryRefreshTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    public LaunchSettingsPage()
    {
        InitializeComponent();
        string memoryGroup = $"GameMemoryMode_{Guid.NewGuid():N}";
        AutoMemoryButton.GroupName = CustomMemoryButton.GroupName = memoryGroup;
        MoreJavaList.ItemTemplate = JavaList.ItemTemplate;
        App.JavaCatalog.Changed += () =>
        {
            RefreshControls();
            RefreshSpecifiedJava();
            UpdateLaunchPreviews();
        };
        InitializeAdvancedControls();
        App.MemoryOptimizer.Changed += RefreshMemoryOptimizationControls;
        LocalizationManager.Changed += (_, _) =>
        {
            RefreshControls();
            RefreshLaunchOptions();
        };
        _memoryRefreshTimer.Tick += (_, _) => RefreshMemoryUsage(animate: true);
        Loaded += (_, _) =>
        {
            RefreshLaunchOptions();
            RefreshSpecifiedJava();
            UpdateMemoryTimer();
        };
        IsVisibleChanged += (_, _) => UpdateMemoryTimer();
        Unloaded += (_, _) =>
        {
            _memoryRefreshTimer.Stop();
            CommitMemoryPercentage();
        };
        RefreshControls();
        RefreshLaunchOptions();
    }

    internal async Task ResetToDefaultsAsync()
    {
        _launchTextSaveTimer.Stop();
        LauncherSettings defaults = new();
        if (!App.UpdateSettings(App.Settings with
            {
                GamePriority = defaults.GamePriority,
                WindowMode = defaults.WindowMode,
                GameWindowWidth = defaults.GameWindowWidth,
                GameWindowHeight = defaults.GameWindowHeight,
                AutoAllocateMemory = defaults.AutoAllocateMemory,
                CustomMemoryPercent = defaults.CustomMemoryPercent,
                OptimizeMemoryBeforeLaunch = defaults.OptimizeMemoryBeforeLaunch,
                GameWindowTitle = defaults.GameWindowTitle,
                JvmArguments = defaults.JvmArguments,
                GameArguments = defaults.GameArguments,
                PreLaunchCommand = defaults.PreLaunchCommand,
                WaitForPreLaunchCommand = defaults.WaitForPreLaunchCommand,
                GarbageCollector = defaults.GarbageCollector,
                DisableJavaLaunchWrapper = defaults.DisableJavaLaunchWrapper,
                DisableLwjglUnsafeAgent = defaults.DisableLwjglUnsafeAgent,
                PreferHighPerformanceGpu = defaults.PreferHighPerformanceGpu,
                VerifyMicrosoftCertificates = defaults.VerifyMicrosoftCertificates,
                SkipFileVerification = defaults.SkipFileVerification
            })) return;

        App.Logger.Information("Settings", "启动选项和内存分配已重置, 继续重置并重新获取 Java 列表.");
        RefreshLaunchOptions();
        await App.JavaCatalog.ResetAsync();
    }

    private void RefreshLaunchOptions()
    {
        _initializingLaunchOptions = true;
        try
        {
            LauncherSettings settings = CurrentSettings;
            ProcessPriorityBox.ItemsSource = new[]
            {
                LocalizationManager.Get("launchSettings.priorityHigh"),
                LocalizationManager.Get("launchSettings.priorityNormal"),
                LocalizationManager.Get("launchSettings.priorityLow")
            };
            ProcessPriorityBox.SelectedIndex = Array.IndexOf(PriorityChoices, settings.GamePriority);
            WindowModeBox.ItemsSource = new[] { "windowFullscreen", "windowDefault", "windowMatchLauncher", "windowCustom", "windowMaximized" }.Select(key => LocalizationManager.Get("launchSettings." + key)).ToArray();
            WindowModeBox.SelectedIndex = Array.IndexOf(WindowChoices, settings.WindowMode);
            GameWindowWidthBox.Text = settings.GameWindowWidth.ToString(CultureInfo.CurrentCulture);
            GameWindowHeightBox.Text = settings.GameWindowHeight.ToString(CultureInfo.CurrentCulture);
            CustomSizeReveal.IsExpanded = settings.WindowMode == GameWindowMode.Custom;
            AutoMemoryButton.IsChecked = settings.AutoAllocateMemory;
            CustomMemoryButton.IsChecked = !settings.AutoAllocateMemory;
            MemoryPercentSlider.Value = settings.CustomMemoryPercent;
            MemoryPercentText.Text = string.Format(LocalizationManager.Get("launchSettings.memoryPercent"), settings.CustomMemoryPercent);
            OptimizeMemoryBeforeLaunchBox.IsChecked = settings.OptimizeMemoryBeforeLaunch;
            RefreshMemoryOptimizationControls();
            RefreshAdvancedControls(settings);
            RefreshVersionControls();
            _dirtyLaunchText.Clear();
            _dirtyWindowSize.Clear();
            _memoryPercentDirty = false;
        }
        finally
        {
            _initializingLaunchOptions = false;
        }
        RefreshMemoryUsage();
        UpdateLaunchPreviews();
    }

    private bool SaveLaunchOptions(LauncherSettings settings, string message)
    {
        if (IsVersionMode) return SaveVersionOptions(_versionOptions.WithSettings(settings, App.Settings));
        if (App.UpdateSettings(settings))
        {
            App.Logger.Information("Settings", message);
            return true;
        }
        RefreshLaunchOptions();
        return false;
    }

    private void ProcessPriorityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLaunchOptions || ProcessPriorityBox.SelectedIndex < 0) return;
        GameProcessPriority priority = PriorityChoices[ProcessPriorityBox.SelectedIndex];
        if (priority == CurrentSettings.GamePriority) return;
        SaveLaunchOptions(CurrentSettings with { GamePriority = priority }, $"游戏进程优先级已保存: {priority}.");
    }

    private void WindowModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLaunchOptions || WindowModeBox.SelectedIndex < 0) return;
        GameWindowMode mode = WindowChoices[WindowModeBox.SelectedIndex];
        if (mode == CurrentSettings.WindowMode) return;
        if (SaveLaunchOptions(CurrentSettings with { WindowMode = mode }, $"游戏窗口模式已保存: {mode}."))
            CustomSizeReveal.IsExpanded = mode == GameWindowMode.Custom;
    }

    private void WindowSizeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        WindowSizeBox_LostFocus(sender, e);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void WindowSizeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_initializingLaunchOptions || _dirtyWindowSize.Count == 0) return;
        var settings = CurrentSettings;
        int width = settings.GameWindowWidth, height = settings.GameWindowHeight;
        if ((_dirtyWindowSize.Contains(GameWindowWidthBox) && !int.TryParse(GameWindowWidthBox.Text, out width)) ||
            (_dirtyWindowSize.Contains(GameWindowHeightBox) && !int.TryParse(GameWindowHeightBox.Text, out height)) ||
            width is < 1 or > 16384 || height is < 1 or > 16384)
        {
            App.Logger.Warning("Settings", $"自定义游戏窗口尺寸无效: {GameWindowWidthBox.Text}, {GameWindowHeightBox.Text}.");
            LauncherToasts.Error(LocalizationManager.Get("launchSettings.invalidWindowSize"));
            RefreshLaunchOptions();
            return;
        }
        _dirtyWindowSize.Clear();
        if (width == settings.GameWindowWidth && height == settings.GameWindowHeight) return;
        SaveLaunchOptions(settings with { GameWindowWidth = width, GameWindowHeight = height },
            $"自定义游戏窗口尺寸已保存: {width} x {height}.");
    }

    private void WindowSizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializingLaunchOptions) _dirtyWindowSize.Add((TextBox)sender);
    }

    private void MemoryMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializingLaunchOptions) return;
        bool automatic = AutoMemoryButton.IsChecked == true;
        int percent = (int)Math.Round(MemoryPercentSlider.Value);
        if (automatic == CurrentSettings.AutoAllocateMemory) return;
        SaveLaunchOptions(CurrentSettings with { AutoAllocateMemory = automatic },
            $"游戏内存分配模式已保存: 自动={automatic}, 自定义可用内存占比={percent}%.");
        UpdateMemoryDisplay();
    }

    private void MemoryPercentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MemoryPercentText is not null)
            MemoryPercentText.Text = string.Format(LocalizationManager.Get("launchSettings.memoryPercent"), (int)Math.Round(e.NewValue));
        if (!_initializingLaunchOptions)
        {
            _memoryPercentDirty = true;
            UpdateMemoryDisplay();
        }
    }

    private void MemoryPercentSlider_Commit(object sender, RoutedEventArgs e) => CommitMemoryPercentage();

    private void MemoryPercentSlider_KeyUp(object sender, KeyEventArgs e) => CommitMemoryPercentage();

    private void CommitMemoryPercentage()
    {
        if (_initializingLaunchOptions || !_memoryPercentDirty || (IsVersionMode && CustomMemoryButton.IsChecked != true)) return;
        _memoryPercentDirty = false;
        int percent = (int)Math.Round(MemoryPercentSlider.Value);
        if (percent == CurrentSettings.CustomMemoryPercent) return;
        SaveLaunchOptions(CurrentSettings with { CustomMemoryPercent = percent }, $"自定义游戏内存占比已保存: 可用内存的 {percent}%.");
    }

    private void UpdateMemoryTimer()
    {
        _memoryRefreshTimer.Stop();
        if (!IsLoaded || !IsVisible) return;
        RefreshMemoryUsage();
        _memoryRefreshTimer.Start();
    }

    private void RefreshMemoryUsage(bool animate = false)
    {
        try
        {
            _memorySnapshot = GameMemoryAllocation.ReadPhysicalMemory();
            _memoryFailureLogged = false;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            _memorySnapshot = null;
            if (!_memoryFailureLogged)
            {
                App.Logger.Error("Settings", "无法读取系统物理内存信息.", error);
                _memoryFailureLogged = true;
            }
        }
        UpdateMemoryDisplay(animate ? _memoryRefreshTimer.Interval : TimeSpan.Zero);
        UpdateLaunchPreviews();
    }

    private void UpdateMemoryDisplay(TimeSpan? transition = null)
    {
        bool automatic = AutoMemoryButton.IsChecked == true;
        int percent = (int)Math.Round(MemoryPercentSlider.Value);
        MemoryModeHintText.Text = automatic
            ? LocalizationManager.Get("launchSettings.memoryAutomaticHint")
            : string.Format(LocalizationManager.Get("launchSettings.memoryCustomHint"), percent);
        MemoryUsageBar.Visibility = _memorySnapshot is null ? Visibility.Collapsed : Visibility.Visible;
        if (_memorySnapshot is not { } memory)
        {
            UsedMemoryText.Text = LocalizationManager.Get("launchSettings.memoryUnavailable");
            GameMemoryText.Text = "";
            return;
        }

        MemoryUnitSystem unit = App.Settings.MemoryUnit;
        ulong gameBytes = GameMemoryAllocation.CalculateBytes(memory.ClampedAvailableBytes, automatic, percent, unit);
        ulong freeBytes = memory.ClampedAvailableBytes - gameBytes;
        MemoryUsageBar.SetValues(memory.UsedBytes, gameBytes, freeBytes, transition ?? TimeSpan.FromMilliseconds(140));
        var gameUnit = DataSizeFormatter.SelectUnit(gameBytes, unit);
        UsedMemoryText.Text = string.Format(LocalizationManager.Get("launchSettings.memoryUsedValue"),
            DataSizeFormatter.Format(memory.UsedBytes, unit), DataSizeFormatter.Format(memory.TotalBytes, unit));
        GameMemoryText.Text = string.Format(LocalizationManager.Get("launchSettings.memoryGameValue"), gameUnit.Convert(gameBytes), gameUnit.Symbol);
    }

    private void MemoryUsageBar_FirstBoundaryChanged(object? sender, EventArgs e) => PositionGameMemoryLabels();

    private void MemoryLabel_SizeChanged(object sender, SizeChangedEventArgs e) => PositionGameMemoryLabels();

    private void PositionGameMemoryLabels()
    {
        if (MemoryUsageBar is null || GameMemoryTitle is null || GameMemoryText is null || UsedMemoryText is null) return;
        double labelWidth = Math.Max(GameMemoryTitle.ActualWidth, GameMemoryText.ActualWidth);
        double maximum = Math.Max(0, MemoryUsageBar.ActualWidth - labelWidth);
        double minimum = Math.Min(maximum, Math.Max(UsedMemoryTitle.ActualWidth, UsedMemoryText.ActualWidth) + 12);
        double left = Math.Clamp(MemoryUsageBar.FirstBoundaryOffset + 2, minimum, maximum);
        Canvas.SetLeft(GameMemoryTitle, left);
        Canvas.SetLeft(GameMemoryText, left);
    }

    private void RefreshControls()
    {
        IReadOnlyList<LauncherJavaInstallation> installed = App.Settings.JavaInstallations;
        JavaList.ItemsSource = installed.Take(PreviewCount).ToArray();
        MoreJavaList.ItemsSource = installed.Skip(PreviewCount).ToArray();
        JavaList.Visibility = installed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        JavaCountText.Text = string.Format(LocalizationManager.Get("java.count"), installed.Count);
        ExpandJavaButton.Visibility = installed.Count > PreviewCount ? Visibility.Visible : Visibility.Collapsed;
        if (installed.Count <= PreviewCount)
        {
            _showAllJava = false;
            _expansionVersion++;
            MoreJavaClip.BeginAnimation(HeightProperty, null);
            MoreJavaClip.BeginAnimation(OpacityProperty, null);
            MoreJavaClip.Height = 0;
            MoreJavaClip.Visibility = Visibility.Collapsed;
        }

        ExpandJavaButton.Content = _showAllJava ? LocalizationManager.Get("java.collapse")
            : string.Format(LocalizationManager.Get("java.expand"), installed.Count - PreviewCount);
        EmptyText.Visibility = installed.Count == 0 && !App.JavaCatalog.IsRefreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !App.JavaCatalog.IsRefreshing;
        AddButton.IsEnabled = !App.JavaCatalog.IsRefreshing;
        RefreshButton.Content = App.JavaCatalog.IsRefreshing ? LocalizationManager.Get("java.searching") : LocalizationManager.Get("java.search");
    }

    private void ExpandJavaButton_Click(object sender, RoutedEventArgs e)
    {
        _showAllJava = !_showAllJava;
        ExpandJavaButton.Content = _showAllJava ? LocalizationManager.Get("java.collapse")
            : string.Format(LocalizationManager.Get("java.expand"), App.Settings.JavaInstallations.Count - PreviewCount);
        AnimateJavaExpansion();
    }

    private void AnimateJavaExpansion()
    {
        _expansionVersion++;
        int version = _expansionVersion;
        double startHeight = MoreJavaClip.Visibility == Visibility.Visible ? MoreJavaClip.ActualHeight : 0;
        double startOpacity = MoreJavaClip.Visibility == Visibility.Visible ? MoreJavaClip.Opacity : 0;
        MoreJavaClip.BeginAnimation(HeightProperty, null);
        MoreJavaClip.BeginAnimation(OpacityProperty, null);
        MoreJavaClip.Height = startHeight;
        MoreJavaClip.Opacity = startOpacity;
        MoreJavaClip.Visibility = Visibility.Visible;

        double width = Math.Max(JavaList.ActualWidth, 300);
        MoreJavaList.Measure(new Size(width, double.PositiveInfinity));
        double targetHeight = _showAllJava ? MoreJavaList.DesiredSize.Height : 0;
        DoubleAnimation height = new(startHeight, targetHeight, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        height.Completed += (_, _) =>
        {
            if (version != _expansionVersion) return;
            MoreJavaClip.BeginAnimation(HeightProperty, null);
            MoreJavaClip.BeginAnimation(OpacityProperty, null);
            MoreJavaClip.Height = _showAllJava ? double.NaN : 0;
            MoreJavaClip.Opacity = 1;
            if (!_showAllJava) MoreJavaClip.Visibility = Visibility.Collapsed;
        };
        MoreJavaClip.BeginAnimation(HeightProperty, height);
        MoreJavaClip.BeginAnimation(OpacityProperty, new DoubleAnimation(startOpacity, _showAllJava ? 1 : 0, TimeSpan.FromMilliseconds(200)));
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await App.JavaCatalog.RefreshAsync(true);

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = LocalizationManager.Get("launchSettings.chooseJavaExecutable"),
            Filter = LocalizationManager.Get("launchSettings.javaExecutableFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            AddButton.IsEnabled = false;
            try
            {
                await App.JavaCatalog.AddAsync(dialog.FileName);
            }
            finally
            {
                AddButton.IsEnabled = true;
            }
        }
    }

    private static LauncherJavaInstallation? Entry(object sender) => (sender as Button)?.Tag as LauncherJavaInstallation;

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is { } java) App.JavaCatalog.Move(java, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is not { } java) return;
        // 折叠时若第三项下移, 展开列表以便继续看到它的新位置.
        if (!_showAllJava && App.Settings.JavaInstallations.Count > PreviewCount && App.Settings.JavaInstallations[PreviewCount - 1].Path == java.Path)
        {
            _showAllJava = true;
            ExpandJavaButton.Content = LocalizationManager.Get("java.collapse");
            // 排序完成后列表内容会刷新, 再启动展开动画.
            App.JavaCatalog.Move(java, 1);
            AnimateJavaExpansion();
            return;
        }

        App.JavaCatalog.Move(java, 1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is { } java) App.JavaCatalog.Remove(java);
    }

    private async void DeleteInstalledJava_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is { } java) await App.JavaCatalog.DeleteInstalledAsync(java);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is not { } java)
        {
            return;
        }

        string? folder = Path.GetDirectoryName(java.Path);
        if (folder is null || !Directory.Exists(folder))
        {
            LauncherToasts.Error(LocalizationManager.Get("launchSettings.javaFolderMissing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            App.Logger.Information("Java", $"打开 Java 文件夹: {folder}.");
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("Java", $"无法打开 Java 文件夹: {folder}.", error);
            LauncherToasts.Error(LocalizationManager.Get("launchSettings.openJavaFolderFailed"));
        }
    }
}
