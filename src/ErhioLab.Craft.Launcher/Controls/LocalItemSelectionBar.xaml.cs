using System.Windows;

namespace ErhioLab.Craft.Launcher.Controls;

public partial class LocalItemSelectionBar
{
    internal event Action? EnableRequested;
    internal event Action? DisableRequested;
    internal event Action? DeleteRequested;
    internal event Action? DeselectRequested;
    internal event Action? LocalInfoRequested;

    public LocalItemSelectionBar() => InitializeComponent();

    internal void UpdateSelection(int count, string text)
    {
        SelectionText.Text = text;
        LocalInfoButton.Visibility = LocalInfoRequested is not null && count == 1 ? Visibility.Visible : Visibility.Collapsed;
        SelectionReveal.IsExpanded = count > 0;
    }

    private void Enable_Click(object sender, RoutedEventArgs e) => EnableRequested?.Invoke();
    private void Disable_Click(object sender, RoutedEventArgs e) => DisableRequested?.Invoke();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
    private void Deselect_Click(object sender, RoutedEventArgs e) => DeselectRequested?.Invoke();
    private void LocalInfo_Click(object sender, RoutedEventArgs e) => LocalInfoRequested?.Invoke();
}
