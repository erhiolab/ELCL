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
        SwitchLibraryTheme(settings.Theme);

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
        Set("AccentBrush", accent);
        double accentBrightness = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255;
        Set("AccentForegroundBrush", accentBrightness > 0.54 ? "#FF0B0E12" : "#FFFFFFFF");
        Set("AccentMutedBrush", Blend(surface, accent, day ? 0.17 : 0.09));
        Set("AccentHoverBrush", Blend(surface, accent, day ? 0.25 : 0.18));
        Set("AccentTextBrush", day ? Blend(Parse("#FF112739"), accent, 0.44) : accent);
    }

    private static void SwitchLibraryTheme(LauncherTheme theme)
    {
        string themeName = theme == LauncherTheme.Day ? "Light" : "Dark";
        Uri themeUri = new(
            $"pack://application:,,,/Wpf.Ui;component/Resources/Theme/{themeName}.xaml",
            UriKind.Absolute);

        if (ReplaceThemeDictionary(Application.Current.Resources, themeName, themeUri))
        {
            return;
        }

        throw new InvalidOperationException("找不到 WPF UI 主题资源字典.");
    }

    private static bool ReplaceThemeDictionary(ResourceDictionary parent, string themeName, Uri themeUri)
    {
        for (int index = 0; index < parent.MergedDictionaries.Count; index++)
        {
            ResourceDictionary dictionary = parent.MergedDictionaries[index];
            string? source = dictionary.Source?.ToString();
            if (source is not null
                && source.Contains("Wpf.Ui;component/Resources/Theme/", StringComparison.OrdinalIgnoreCase))
            {
                if (!source.EndsWith($"/{themeName}.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    // 只替换颜色资源, 不调用会修改窗口背景的 ApplicationThemeManager.Apply.
                    parent.MergedDictionaries[index] = new ResourceDictionary { Source = themeUri };
                }

                return true;
            }

            if (ReplaceThemeDictionary(dictionary, themeName, themeUri))
            {
                return true;
            }
        }

        return false;
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
