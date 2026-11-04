using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>Shared settings and toolbox card with a header, divider, and content.</summary>
public class LauncherCard : HeaderedContentControl
{
    static LauncherCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LauncherCard),
            new System.Windows.FrameworkPropertyMetadata(typeof(LauncherCard)));
    }
}
