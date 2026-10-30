using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Navigation;

namespace ErhioLab.Craft.Launcher.Views.Shared;

/// <summary>显示下载/设置和更多页面共用的侧栏结构.</summary>
public partial class SectionSidebarView
{
    private RadioButton? _selectedItem;
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
        NavigationItems.ItemsSource = navigationItems.Select((item, index) => new SectionSidebarEntry(item, index == 0, groupName)).ToArray();
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
        if (_selectedItem is { IsLoaded: true })
        {
            MoveSelection(_selectedItem, false);
        }
    }

    private void NavigationItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (_indicatorReady && sender is RadioButton { IsChecked: true } button)
        {
            _selectedItem = button;
            MoveSelection(button, false);
        }
    }

    private void NavigationItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button)
        {
            return;
        }

        _selectedItem = button;
        App.Logger.Information("Navigation", $"选择侧栏项目: {button.Content}");
        if (button.Tag is SectionSidebarItem item)
        {
            SelectionChanged?.Invoke(item);
        }

        if (_indicatorReady && button.IsLoaded)
        {
            MoveSelection(button, SidebarIndicator.Opacity > 0);
        }
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
