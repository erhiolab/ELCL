using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Views.Shared;

public partial class PlaceholderPage : UserControl
{
    public PlaceholderPage(string code, string title, string description)
    {
        InitializeComponent();

        PageCodeText.Text = code;
        PageTitleText.Text = title;
        PageDescriptionText.Text = description;
    }
}