using System.ComponentModel;
using System.IO;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Versions;

public sealed class LocalMinecraftPackRow(string path, MinecraftPackKind kind, bool? enabled = null) : INotifyPropertyChanged
{
    private LocalMinecraftPack? _pack;
    private MinecraftPackFormat? _clientFormat;
    internal string Path { get; } = path;
    internal bool? Enabled { get; } = enabled;
    public bool CanToggle => Enabled.HasValue;
    public bool IsShaderPack => kind == MinecraftPackKind.ShaderPack;
    public bool HasShaderConfig { get; private set; }

    internal void SetHasShaderConfig(bool hasConfig)
    {
        HasShaderConfig = hasConfig;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasShaderConfig)));
    }

    public string ToggleLabel => LocalizationManager.Get(Enabled == true ? "mods.disable" : "mods.enable");
    public Geometry ToggleIcon => Enabled == true ? LauncherIcons.Disable : LauncherIcons.Check;
    public double IconOpacity => Enabled == false ? 0.4 : 1;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Name => _pack?.Name ?? FileName;
    public ImageSource? Icon { get; private set; }
    public Geometry FallbackIcon => _pack?.Error is not null ? LauncherIcons.Info : Enabled == false ? LauncherIcons.Disable : kind == MinecraftPackKind.ResourcePack ? LauncherIcons.Image : LauncherIcons.DayTheme;

    public IReadOnlyList<MinecraftTextSpan> Description => _pack?.Description is { Count: > 0 } description ? description : [new(_pack?.Error is not null ? L("invalid") : L(_pack is null ? "reading" : "noDescription"))];

    public string Summary => string.Join(" · ", new[] { Enabled switch { true => L("enabled"), false => LocalizationManager.Get("mods.disabled"), _ => L("stateUnknown") }, _pack?.Version, _pack?.Author, _pack is null ? FileName : Versions }.Where(text => !string.IsNullOrWhiteSpace(text)));

    public string Details => string.Format(L("detailsText"), _pack?.Author ?? L("notDeclared"), _pack?.Version ?? L("notDeclared"), _pack?.MinecraftVersions ?? L("notDeclared"), _pack?.Size is { } size ? DataSizeFormatter.Format(size, App.Settings.MemoryUnit, "0.##", minimumPower: 0) : L("folder"), Path);

    public string FormatText => kind == MinecraftPackKind.ResourcePack ? string.Format(L("packFormat"), FormatRange) : "";
    public string Error => _pack?.Error ?? "";

    public string Compatibility => _clientFormat is { } format && (_pack?.MinimumFormat is not null || _pack?.MaximumFormat is not null) ? string.Format(L(IsInRange(_pack, format) ? "matches" : "doesNotMatch"), Format(format)) : "";

    private string Versions => _pack?.MinecraftVersions is { } declared ? string.Format(L("gameVersions"), declared) : kind == MinecraftPackKind.ResourcePack && (_pack?.MinimumFormat is not null || _pack?.MaximumFormat is not null) ? FormatText : L("noVersionRange");

    private string FormatRange => (_pack?.MinimumFormat, _pack?.MaximumFormat) switch
    {
        ({ } minimum, { } maximum) when minimum == maximum => Format(minimum),
        ({ } minimum, { } maximum) => Format(minimum) + " - " + Format(maximum),
        ({ } minimum, null) => string.Format(L("minimum"), Format(minimum)),
        (null, { } maximum) => string.Format(L("maximum"), Format(maximum)),
        _ => L("notDeclared")
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Apply(LocalMinecraftPack pack, ImageSource? icon)
    {
        _pack = pack;
        Icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    internal void SetClientFormat(MinecraftPackFormat? format)
    {
        _clientFormat = format;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Compatibility)));
    }

    internal bool Matches(string filter) => Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                            || FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) || _pack?.Author?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsInRange(LocalMinecraftPack? pack, MinecraftPackFormat format)
    {
        var current = (format.Major, format.Minor ?? 0);
        return (pack?.MinimumFormat is not { } minimum || current.CompareTo((minimum.Major, minimum.Minor ?? 0)) >= 0)
               && (pack?.MaximumFormat is not { } maximum || current.CompareTo((maximum.Major, maximum.Minor ?? int.MaxValue)) <= 0);
    }

    private static string Format(MinecraftPackFormat format) => format.Minor is { } minor ? $"{format.Major}.{minor}" : format.Major.ToString();
    private static string L(string key) => LocalizationManager.Get("localPacks." + key);
}
