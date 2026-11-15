namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>模组列表共用的名称、原名、版本和标签信息.</summary>
public partial class ModIdentityText
{
    public ModIdentityText() => InitializeComponent();

    internal bool ShowSummary
    {
        set => SummaryLine.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
}
