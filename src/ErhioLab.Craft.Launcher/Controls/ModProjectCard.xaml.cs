namespace ErhioLab.Craft.Launcher.Controls;

public partial class ModProjectCard
{
    public ModProjectCard() => InitializeComponent();

    internal bool ShowSummary
    {
        set => Identity.ShowSummary = value;
    }
}
