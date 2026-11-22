using System.Windows;
using System.Windows.Markup;

namespace ErhioLab.Craft.Launcher.Controls;

[ContentProperty(nameof(Body))]
public partial class LocalItemInfoCard
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(LocalItemInfoCard));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(FrameworkElement), typeof(LocalItemInfoCard));

    public string? Heading
    {
        get => (string?)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public FrameworkElement? Body
    {
        get => (FrameworkElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public event RoutedEventHandler? CloseRequested;

    public LocalItemInfoCard() => InitializeComponent();

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
}
