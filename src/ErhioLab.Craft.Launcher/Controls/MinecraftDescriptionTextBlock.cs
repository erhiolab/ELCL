using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Controls;

public sealed class MinecraftDescriptionTextBlock : TextBlock
{
    public static readonly DependencyProperty SpansProperty = DependencyProperty.Register(nameof(Spans),
        typeof(IReadOnlyList<MinecraftTextSpan>), typeof(MinecraftDescriptionTextBlock), new PropertyMetadata(null, Update));

    public static readonly DependencyProperty SingleLineProperty = DependencyProperty.Register(nameof(SingleLine),
        typeof(bool), typeof(MinecraftDescriptionTextBlock), new PropertyMetadata(false, Update));

    public IReadOnlyList<MinecraftTextSpan>? Spans
    {
        get => (IReadOnlyList<MinecraftTextSpan>?)GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    public bool SingleLine
    {
        get => (bool)GetValue(SingleLineProperty);
        set => SetValue(SingleLineProperty, value);
    }

    private static void Update(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (MinecraftDescriptionTextBlock)sender;
        block.Inlines.Clear();
        foreach (var span in block.Spans ?? [])
        {
            string text = block.SingleLine ? span.Text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ') : span.Text;
            var run = new Run(text)
            {
                FontWeight = span.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = span.Italic ? FontStyles.Italic : FontStyles.Normal
            };
            if (span.Color is { } hex && ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                run.Foreground = brush;
            }

            if (span.Underlined || span.Strikethrough)
            {
                TextDecorationCollection decorations = [];
                if (span.Underlined) decorations.Add(System.Windows.TextDecorations.Underline);
                if (span.Strikethrough) decorations.Add(System.Windows.TextDecorations.Strikethrough);
                run.TextDecorations = decorations;
            }

            block.Inlines.Add(run);
        }
    }
}
