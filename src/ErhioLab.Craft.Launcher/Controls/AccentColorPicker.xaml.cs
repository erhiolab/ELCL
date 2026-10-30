using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>可复用的 RGB 主题色选择器, 同步 HEX 输入与 HSV 拉条.</summary>
public partial class AccentColorPicker
{
    private bool _syncing = true;

    /// <summary>用户确认颜色时触发.</summary>
    internal event Action<string>? ColorSelected;

    /// <summary>初始化颜色选择器.</summary>
    public AccentColorPicker()
    {
        InitializeComponent();
        SetColor("#80CEFF");
    }

    /// <summary>从外部配置更新当前颜色和所有选择控件.</summary>
    /// <param name="hexColor">六位 RGB 颜色.</param>
    internal void SetColor(string hexColor)
    {
        if (!TryParseHex(hexColor, out Color color))
        {
            throw new ArgumentException("主题色必须使用 #RRGGBB 格式.", nameof(hexColor));
        }

        _syncing = true;
        (double hue, double saturation, double brightness) = ToHsv(color);
        HueSlider.Value = hue;
        SaturationSlider.Value = saturation;
        BrightnessSlider.Value = brightness;
        HexInput.Text = Format(color);
        PreviewSwatch.Background = new SolidColorBrush(color);
        CurrentSwatch.Background = new SolidColorBrush(color);
        HexErrorText.Visibility = Visibility.Collapsed;
        _syncing = false;
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
        {
            ColorSelected?.Invoke(color);
            PickerButton.IsChecked = false;
        }
    }

    private void PickerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || PreviewSwatch is null || HexInput is null)
        {
            return;
        }

        Color color = FromHsv(HueSlider.Value, SaturationSlider.Value, BrightnessSlider.Value);
        _syncing = true;
        HexInput.Text = Format(color);
        PreviewSwatch.Background = new SolidColorBrush(color);
        HexErrorText.Visibility = Visibility.Collapsed;
        _syncing = false;
    }

    private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || !TryParseHex(HexInput.Text.Trim(), out Color color))
        {
            return;
        }

        // HEX 输入有效时只同步拉条和预览, 点击应用后才写入配置.
        _syncing = true;
        (double hue, double saturation, double brightness) = ToHsv(color);
        HueSlider.Value = hue;
        SaturationSlider.Value = saturation;
        BrightnessSlider.Value = brightness;
        PreviewSwatch.Background = new SolidColorBrush(color);
        HexErrorText.Visibility = Visibility.Collapsed;
        _syncing = false;
    }

    private void HexInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyColor();
        }
    }

    private void ApplyColor_Click(object sender, RoutedEventArgs e) => ApplyColor();

    private void ApplyColor()
    {
        if (!TryParseHex(HexInput.Text.Trim(), out Color color))
        {
            HexErrorText.Visibility = Visibility.Visible;
            return;
        }

        ColorSelected?.Invoke(Format(color));
        PickerButton.IsChecked = false;
    }

    private static bool TryParseHex(string value, out Color color)
    {
        color = default;
        if (value.Length != 7 || value[0] != '#'
                              || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                  out uint rgb))
        {
            return false;
        }

        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static (double Hue, double Saturation, double Brightness) ToHsv(Color color)
    {
        double red = color.R / 255.0;
        double green = color.G / 255.0;
        double blue = color.B / 255.0;
        byte maximumChannel = Math.Max(color.R, Math.Max(color.G, color.B));
        byte minimumChannel = Math.Min(color.R, Math.Min(color.G, color.B));
        double maximum = maximumChannel / 255.0;
        double minimum = minimumChannel / 255.0;
        double difference = maximum - minimum;
        double hue = 0;

        if (difference > 0)
        {
            if (maximumChannel == color.R)
            {
                hue = 60 * ((green - blue) / difference % 6);
            }
            else if (maximumChannel == color.G)
            {
                hue = 60 * ((blue - red) / difference + 2);
            }
            else
            {
                hue = 60 * ((red - green) / difference + 4);
            }
        }

        return ((hue + 360) % 360, maximumChannel == 0 ? 0 : difference / maximum * 100, maximum * 100);
    }

    private static Color FromHsv(double hue, double saturation, double brightness)
    {
        double chroma = brightness / 100 * saturation / 100;
        double hueSection = hue / 60;
        double secondary = chroma * (1 - Math.Abs(hueSection % 2 - 1));
        double offset = brightness / 100 - chroma;
        (double red, double green, double blue) = hueSection switch
        {
            < 1 => (chroma, secondary, 0.0),
            < 2 => (secondary, chroma, 0.0),
            < 3 => (0.0, chroma, secondary),
            < 4 => (0.0, secondary, chroma),
            < 5 => (secondary, 0.0, chroma),
            _ => (chroma, 0.0, secondary)
        };

        return Color.FromRgb(
            (byte)Math.Round((red + offset) * 255),
            (byte)Math.Round((green + offset) * 255),
            (byte)Math.Round((blue + offset) * 255)
        );
    }
}
