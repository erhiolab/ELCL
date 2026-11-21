using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Navigation;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Shared;

/// <summary>显示下载/设置和更多页面共用的侧栏结构.</summary>
public partial class SectionSidebarView
{
    private readonly SectionSidebarEntry[] _entries;
    private SectionSidebarEntry? _selection;
    private RadioButton? _selectedButton;
    private bool _indicatorReady;

    /// <summary>用户点击侧栏条目的悬浮操作按钮.</summary>
    internal event Action<SectionSidebarItem>? ActionRequested;

    /// <summary>用户切换侧栏条目时通知所属页面.</summary>
    internal event Action<SectionSidebarItem>? SelectionChanged;

    /// <summary>初始化一级页面的侧栏内容.</summary>
    /// <param name="navigationItems">带图标的侧栏导航项.</param>
    internal SectionSidebarView(params SectionSidebarItem[] navigationItems)
    {
        InitializeComponent();

        // 不同一级页面各自保留选中项, 不能共享 RadioButton 的组名.
        string groupName = $"SectionNavigation_{Guid.NewGuid():N}";
        _entries = navigationItems.Select((item, index) => new SectionSidebarEntry(item, index == 0, groupName)).ToArray();
        _selection = _entries.FirstOrDefault();
        NavigationItems.ItemsSource = _entries;
    }

    internal bool ApplyFeatureVisibility(LauncherFeatureVisibility features)
    {
        bool changed = false;
        foreach (SectionSidebarEntry entry in _entries)
        {
            Visibility visibility = features.IsHidden(entry.Item.HiddenFeature) ? Visibility.Collapsed : Visibility.Visible;
            changed |= entry.Visibility != visibility;
            entry.Visibility = visibility;
        }

        if (_selection?.Visibility != Visibility.Visible
            && _entries.FirstOrDefault(entry => entry.Visibility == Visibility.Visible) is { } fallback)
            SelectEntry(fallback);

        if (changed && IsLoaded)
            _ = Dispatcher.InvokeAsync(() => UpdateSelectionIndicator(animate: true), DispatcherPriority.Loaded);
        return _selection?.Visibility == Visibility.Visible;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SectionSidebarItem item })
        {
            ActionRequested?.Invoke(item);
        }
    }

    private void Sidebar_Loaded(object sender, RoutedEventArgs e)
    {
        _indicatorReady = true;
        UpdateSelectionIndicator(animate: false);
    }

    private void NavigationItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: SectionSidebarEntry entry, IsChecked: true } button && ReferenceEquals(entry, _selection))
        {
            _selectedButton = button;
            UpdateSelectionIndicator(animate: false);
        }
    }

    private void NavigationItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { DataContext: SectionSidebarEntry { Visibility: Visibility.Visible } entry } button) return;

        SelectEntry(entry);
        _selectedButton = button;
        UpdateSelectionIndicator(animate: SidebarIndicator.Opacity > 0);
    }

    private void SelectEntry(SectionSidebarEntry? entry)
    {
        if (ReferenceEquals(entry, _selection)) return;
        SectionSidebarEntry? previous = _selection;
        _selection = entry;
        _selectedButton = null;
        previous?.IsSelected = false;
        if (entry is not null)
        {
            entry.IsSelected = true;
            App.Logger.Information("Navigation", $"选择侧栏项目: {entry.Title}");
            SelectionChanged?.Invoke(entry.Item);
        }
    }

    private void UpdateSelectionIndicator(bool animate)
    {
        if (!_indicatorReady) return;
        if (_selectedButton is { IsLoaded: true, IsVisible: true } button && NavigationItems.IsAncestorOf(button))
        {
            MoveSelection(button, animate && SidebarIndicator.Opacity > 0);
            return;
        }

        SelectionHighlight.BeginAnimation(OpacityProperty, null);
        SelectionHighlight.Opacity = SidebarIndicator.Opacity = 0;
    }

    private void MoveSelection(RadioButton button, bool animate)
    {
        double itemTop = button.TransformToAncestor(NavigationItems).Transform(new Point(0, 0)).Y;
        double indicatorY = itemTop + (button.ActualHeight - SidebarIndicator.Height) / 2;

        if (!animate)
        {
            SelectionHighlightOffset.BeginAnimation(TranslateTransform.YProperty, null);
            SidebarIndicatorOffset.BeginAnimation(TranslateTransform.YProperty, null);
            SelectionHighlight.BeginAnimation(OpacityProperty, null);
            SelectionHighlightOffset.Y = itemTop;
            SidebarIndicatorOffset.Y = indicatorY;
            SelectionHighlight.Opacity = 1;
            SidebarIndicator.Opacity = 1;
            return;
        }

        AnimateOffset(SelectionHighlightOffset, itemTop);
        AnimateOffset(SidebarIndicatorOffset, indicatorY);
        SelectionHighlight.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new LinearDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)))
            },
            FillBehavior = FillBehavior.Stop
        });
    }

    private static void AnimateOffset(TranslateTransform offset, double targetY)
    {
        double currentY = offset.Y;
        offset.BeginAnimation(TranslateTransform.YProperty, null);
        offset.Y = targetY;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = currentY,
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 },
            FillBehavior = FillBehavior.Stop
        });
    }
}
