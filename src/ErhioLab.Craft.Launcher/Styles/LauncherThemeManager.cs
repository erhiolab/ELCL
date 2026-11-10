using System.Windows;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Styles;

/// <summary>将与平台无关的外观设置应用到 WPF 共享画刷.</summary>
internal static class LauncherThemeManager
{
    /// <summary>更新已打开窗口和页面共用的配色.</summary>
    /// <param name="settings">已经规范化的外观设置.</param>
    internal static void Apply(LauncherSettings settings)
    {
        bool day = settings.Theme == LauncherTheme.Day;
        Color accent = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
        Color surface = Parse(day ? "#FFF7FAFD" : "#FF10131A");

        Set("InkBrush", day ? "#FFF0F5F9" : "#FF0B0D12");
        Set("InkRaisedBrush", surface);
        Set("PanelBrush", day ? "#FFFFFFFF" : "#FF141820");
        Set("PanelHoverBrush", day ? "#FFE8F0F7" : "#FF1B202A");
        Set("LineBrush", day ? "#FFD4E0E9" : "#FF292F39");
        Set("LineSoftBrush", day ? "#FFE2EBF2" : "#FF1D222B");
        Set("TextBrush", day ? "#FF1B2733" : "#FFF1F3F5");
        Set("TextMutedBrush", day ? "#FF647587" : "#FF7F8998");
        Set("DangerTextBrush", day ? "#FFBD2D37" : "#FFFF717D");
        Set("WarningTextBrush", day ? "#FF916208" : "#FFE6BD6A");
        Set("AccentBrush", accent);
        double accentBrightness = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255;
        Set("AccentForegroundBrush", accentBrightness > 0.54 ? "#FF0B0E12" : "#FFFFFFFF");
        Set("AccentMutedBrush", Blend(surface, accent, day ? 0.17 : 0.09));
        Set("AccentHoverBrush", Blend(surface, accent, day ? 0.25 : 0.18));
        Set("AccentTextBrush", day ? Blend(Parse("#FF112739"), accent, 0.44) : accent);

        Color memoryAccent = accentBrightness < 0.2 ? Blend(accent, Colors.White, 0.35) : accentBrightness > 0.8 ? Blend(accent, Colors.Black, 0.2) : accent;
        byte minimum = Math.Min(memoryAccent.R, Math.Min(memoryAccent.G, memoryAccent.B));
        int chroma = Math.Max(memoryAccent.R, Math.Max(memoryAccent.G, memoryAccent.B)) - minimum;
        Color saturatedAccent = chroma < 32 ? memoryAccent : Color.FromRgb(
            (byte)((memoryAccent.R - minimum) * 255 / chroma),
            (byte)((memoryAccent.G - minimum) * 255 / chroma),
            (byte)((memoryAccent.B - minimum) * 255 / chroma));
        Set("MemoryUsedBrush", Blend(Colors.Black, saturatedAccent, day ? 0.72 : 0.8));
        Set("MemoryAllocatedBrush", memoryAccent);
        Set("MemoryFreeBrush", Blend(Colors.White, memoryAccent, day ? 0.24 : 0.4));
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static Color Blend(Color baseColor, Color accent, double amount) => Color.FromRgb(
        (byte)Math.Round(baseColor.R * (1 - amount) + accent.R * amount),
        (byte)Math.Round(baseColor.G * (1 - amount) + accent.G * amount),
        (byte)Math.Round(baseColor.B * (1 - amount) + accent.B * amount));

    private static void Set(string key, string color) => Set(key, Parse(color));

    private static void Set(string key, Color color)
    {
        // WPF 会冻结从 XAML 加载的画刷; 替换资源后 DynamicResource 才能通知现有控件.
        foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries.Reverse())
        {
            if (dictionary.Contains(key))
            {
                dictionary[key] = new SolidColorBrush(color);
                return;
            }
        }

        throw new InvalidOperationException($"缺少主题资源: {key}");
    }
}
