using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Navigation;

namespace ErhioLab.Craft.Launcher.Views.Shared;

/// <summary>显示下载/设置和更多页面共用的侧栏结构.</summary>
public partial class SectionSidebarView : UserControl
{
    private RadioButton? _selectedItem;
    private bool _indicatorReady;

    /// <summary>初始化一级页面的侧栏内容.</summary>
    /// <param name="navigationItems">带图标的侧栏导航项.</param>
    internal SectionSidebarView(params SectionSidebarItem[] navigationItems)
    {
        InitializeComponent();

        NavigationItems.ItemsSource = navigationItems.Select((item, index) => new
        {
            item.Title,
            item.Icon,
            IsFirst = index == 0
        });
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
