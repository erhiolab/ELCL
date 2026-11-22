using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

internal sealed class MinecraftPackText
{
    private static readonly string[] ColorNames =
    [
        "black", "dark_blue", "dark_green", "dark_aqua", "dark_red", "dark_purple", "gold", "gray",
        "dark_gray", "blue", "green", "aqua", "red", "light_purple", "yellow", "white"
    ];

    private static readonly string[] Colors =
    [
        "#000000", "#0000AA", "#00AA00", "#00AAAA", "#AA0000", "#AA00AA", "#FFAA00", "#AAAAAA",
        "#555555", "#5555FF", "#55FF55", "#55FFFF", "#FF5555", "#FF55FF", "#FFFF55", "#FFFFFF"
    ];

    private readonly List<MinecraftTextSpan> _spans = [];
    private int _remaining = 4096;
    private bool Full => _remaining == 0 || _spans.Count >= 512;

    internal static IReadOnlyList<MinecraftTextSpan> Parse(JsonElement description)
    {
        MinecraftPackText text = new();
        text.Read(description, new(""));
        return text._spans.ToArray();
    }

    internal static IReadOnlyList<MinecraftTextSpan> Parse(string description)
    {
        MinecraftPackText text = new();
        text.Legacy(description, new(""));
        return text._spans.ToArray();
    }

    private MinecraftTextSpan Read(JsonElement component, MinecraftTextSpan inherited)
    {
        if (Full) return inherited;
        if (component.ValueKind == JsonValueKind.Array)
        {
            bool first = true;
            foreach (var child in component.EnumerateArray())
            {
                var applied = Read(child, inherited);
                if (first) inherited = applied;
                first = false;
                if (Full) break;
            }

            return inherited;
        }

        if (component.ValueKind == JsonValueKind.String)
        {
            Legacy(component.GetString()!, inherited);
            return inherited;
        }

        if (component.ValueKind != JsonValueKind.Object) return inherited;

        string? color = String(component, "color");
        var style = inherited with
        {
            Color = color is null ? inherited.Color : Color(color) ?? inherited.Color,
            Bold = Flag("bold", inherited.Bold), Italic = Flag("italic", inherited.Italic),
            Underlined = Flag("underlined", inherited.Underlined), Strikethrough = Flag("strikethrough", inherited.Strikethrough)
        };
        if ((String(component, "text") ?? String(component, "fallback") ?? String(component, "translate")) is { } value) Legacy(value, style);
        if (component.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
            foreach (var child in extra.EnumerateArray())
            {
                Read(child, style);
                if (Full) break;
            }

        return style;

        bool Flag(string key, bool previous) => component.TryGetProperty(key, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true, JsonValueKind.False => false, _ => previous
            }
            : previous;
    }

    private void Legacy(string value, MinecraftTextSpan baseline)
    {
        MinecraftTextSpan active = baseline;
        int start = 0;
        for (int i = 0; !Full && i + 1 < value.Length; i++)
        {
            if (value[i] != '\u00a7') continue;
            char code = char.ToLowerInvariant(value[i + 1]);
            int index = "0123456789abcdef".IndexOf(code);
            var next = index >= 0
                ? new MinecraftTextSpan("", Colors[index])
                : code switch
                {
                    'r' => baseline, 'l' => active with { Bold = true }, 'o' => active with { Italic = true },
                    'n' => active with { Underlined = true }, 'm' => active with { Strikethrough = true }, 'k' => active, _ => null
                };
            if (next is null) continue;
            Add(value.AsSpan(start, i - start), active);
            active = next;
            start = ++i + 1;
        }

        Add(value.AsSpan(start), active);
    }

    private void Add(ReadOnlySpan<char> text, MinecraftTextSpan style)
    {
        if (Full || text.IsEmpty) return;
        int length = Math.Min(text.Length, _remaining);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        _remaining -= length;
        if (length > 0) _spans.Add(style with { Text = text[..length].ToString() });
    }

    internal static string? String(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object
                                                                    && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;

    private static string? Color(string value)
    {
        if (value.Length == 7 && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0) return value;
        int index = Array.IndexOf(ColorNames, value);
        return index < 0 ? null : Colors[index];
    }
}
