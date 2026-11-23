using System.Windows;
using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Controls;

public partial class LocalItemSelectionBar
{
    private readonly List<(Button Button, Func<int, bool>? Visible, Func<bool>? Enabled)> _extraActions = [];

    internal event Action? EnableRequested;
    internal event Action? DisableRequested;
    internal event Action? DeleteRequested;
    internal event Action? DeselectRequested;
    internal event Action? LocalInfoRequested;

    public LocalItemSelectionBar() => InitializeComponent();

    internal void AddAction(string labelKey, Action execute, Func<int, bool>? visible = null, Func<bool>? enabled = null)
    {
        var button = new Button { Margin = DeselectButton.Margin };
        button.SetResourceReference(StyleProperty, "CompactActionButtonStyle");
        button.SetResourceReference(ContentControl.ContentProperty, "I18n." + labelKey);
        button.Click += (_, _) => execute();
        ActionsPanel.Children.Insert(ActionsPanel.Children.IndexOf(DeselectButton), button);
        _extraActions.Add((button, visible, enabled));
    }

    internal void UpdateSelection(int count, string text, bool canChangeEnabled = true)
    {
        SelectionText.Text = text;
        LocalInfoButton.Visibility = LocalInfoRequested is not null && count == 1 ? Visibility.Visible : Visibility.Collapsed;
        EnableButton.Visibility = EnableRequested is not null ? Visibility.Visible : Visibility.Collapsed;
        DisableButton.Visibility = DisableRequested is not null ? Visibility.Visible : Visibility.Collapsed;
        EnableButton.IsEnabled = DisableButton.IsEnabled = canChangeEnabled;
        foreach (var (button, visible, enabled) in _extraActions)
        {
            button.Visibility = (visible?.Invoke(count) ?? count > 0) ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = enabled?.Invoke() ?? true;
        }
        SelectionReveal.IsExpanded = count > 0;
    }

    private void Enable_Click(object sender, RoutedEventArgs e) => EnableRequested?.Invoke();
    private void Disable_Click(object sender, RoutedEventArgs e) => DisableRequested?.Invoke();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
    private void Deselect_Click(object sender, RoutedEventArgs e) => DeselectRequested?.Invoke();
    private void LocalInfo_Click(object sender, RoutedEventArgs e) => LocalInfoRequested?.Invoke();
}
