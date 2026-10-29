using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Views.Shared;

/// <summary>显示下载/设置和更多页面共用的侧栏结构.</summary>
public partial class SectionSidebarView : UserControl
{
    /// <summary>初始化一级页面的侧栏内容.</summary>
    /// <param name="code">页面编号和英文标识.</param>
    /// <param name="title">侧栏标题.</param>
    /// <param name="description">侧栏说明.</param>
    /// <param name="navigationItems">侧栏导航项名称.</param>
    public SectionSidebarView(
        string code,
        string title,
        string description,
        params string[] navigationItems)
    {
        InitializeComponent();

        SectionCodeText.Text = code;
        SectionTitleText.Text = title;
        SectionDescriptionText.Text = description;
        NavigationItems.ItemsSource = navigationItems.Select(
            (item, index) => new
            {
                Index = (index + 1).ToString("D2"),
                Title = item,
                IsFirst = index == 0
            });
    }
}
