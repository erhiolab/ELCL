using System.Globalization;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>按选定进制显示容量和速度; 分组数值可以共用同一单位.</summary>
internal static class DataSizeFormatter
{
    private static readonly string[] BinarySymbols = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
    private static readonly string[] DecimalSymbols = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];

    internal static DataSizeUnit SelectUnit(double referenceBytes, MemoryUnitSystem system, int minimumPower = 3)
    {
        if (!double.IsFinite(referenceBytes)) throw new ArgumentOutOfRangeException(nameof(referenceBytes));
        if (minimumPower is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(minimumPower));
        bool binary = system != MemoryUnitSystem.Decimal;
        double radix = binary ? 1024 : 1000;
        string[] symbols = binary ? BinarySymbols : DecimalSymbols;
        int power = minimumPower;
        double divisor = Math.Pow(radix, power);
        while (power < symbols.Length - 1 && Math.Abs(referenceBytes) >= divisor * radix)
        {
            divisor *= radix;
            power++;
        }

        return new DataSizeUnit(divisor, symbols[power]);
    }

    internal static string Format(double bytes, MemoryUnitSystem system, string numberFormat = "0.0", int minimumPower = 3) =>
        SelectUnit(bytes, system, minimumPower).Format(bytes, numberFormat);
}

internal readonly record struct DataSizeUnit(double BytesPerUnit, string Symbol)
{
    internal double Convert(double bytes) => bytes / BytesPerUnit;

    internal string Format(double bytes, string numberFormat = "0.0") => $"{Convert(bytes).ToString(numberFormat, CultureInfo.CurrentCulture)} {Symbol}";
}
