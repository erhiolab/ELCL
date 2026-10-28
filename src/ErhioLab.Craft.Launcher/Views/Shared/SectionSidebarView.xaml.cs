using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Views.Shared;

public partial class SectionSidebarView : UserControl
{
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
        NavigationItems.ItemsSource = navigationItems.Select((item, index) => new
        {
            Index = (index + 1).ToString("D2"),
            Title = item,
            IsFirst = index == 0
        });
    }
}