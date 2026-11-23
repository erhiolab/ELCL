using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Settings;

public partial class FeatureVisibilityCard
{
    private readonly CheckBox[] _options;

    public FeatureVisibilityCard()
    {
        InitializeComponent();
        _options = OptionGroups.Children.OfType<Grid>()
            .SelectMany(row => row.Children.OfType<WrapPanel>())
            .SelectMany(panel => panel.Children.OfType<CheckBox>()).ToArray();
    }

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        App.FeatureVisibility.Changed += Refresh;
        Refresh();
    }

    private void Card_Unloaded(object sender, RoutedEventArgs e) => App.FeatureVisibility.Changed -= Refresh;

    private void Refresh()
    {
        var features = App.FeatureVisibility;
        foreach (CheckBox box in _options)
        {
            if (box.Tag is not LauncherHiddenFeatures feature) continue;
            box.IsChecked = features.IsConfiguredHidden(feature);
            box.IsEnabled = !features.IsParentConfiguredHidden(feature);
        }

        Card.SetResourceReference(HeaderedContentControl.HeaderProperty, features.IsTemporarilyRevealed
            ? "I18n.featureVisibility.temporarilyDisabled"
            : "I18n.featureVisibility.title");
        Visibility = features.IsHidden(LauncherHiddenFeatures.FeatureVisibility) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { IsEnabled: true, Tag: LauncherHiddenFeatures feature } box) return;
        LauncherHiddenFeatures current = App.Settings.HiddenFeatures;
        LauncherHiddenFeatures updated = box.IsChecked == true ? current | feature : current & ~feature;
        if (updated != current && !App.UpdateSettings(App.Settings with { HiddenFeatures = updated })) Refresh();
    }
}
