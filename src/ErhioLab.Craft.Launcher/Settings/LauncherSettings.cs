using System.Globalization;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>启动器的外观和下载配置, 由客户端定义和校验.</summary>
public sealed record LauncherSettings
{
    /// <summary>当前明暗主题.</summary>
    public LauncherTheme Theme { get; init; } = LauncherTheme.Day;

    /// <summary>六位 RGB 主题色, 默认值为 #80CEFF.</summary>
    public string AccentColor { get; init; } = "#80CEFF";

    /// <summary>窗口不透明度, 范围为 0.40 到 1.00.</summary>
    public double WindowOpacity { get; init; } = 1.0;

    /// <summary>自绘标题栏左侧文字的显示方式.</summary>
    public TitleBarMode TitleBarMode { get; init; } = TitleBarMode.Default;

    /// <summary>标题栏选择文本模式时显示的内容.</summary>
    public string? TitleBarText { get; init; } = "ELCL";

    /// <summary>版本列表优先使用官方源, 失败时自动尝试镜像源.</summary>
    public VersionListSource VersionListSource { get; init; } = VersionListSource.OfficialFirst;

    /// <summary>单个下载任务最多同时使用的连接数, 范围为 1 到 256.</summary>
    public int DownloadMaxConnections { get; init; } = 8;

    /// <summary>下载任务的速度上限, 单位为 MiB/s; 0 表示不限速.</summary>
    public double DownloadSpeedLimitMiBPerSecond { get; init; }

    /// <summary>将外部配置值限制在启动器支持的范围内.</summary>
    /// <returns>规范化后的设置.</returns>
    public LauncherSettings Normalize()
    {
        string accent = AccentColor.Trim();
        string titleText = TitleBarText ?? string.Empty;
        bool validAccent = accent.Length == 7 && accent[0] == '#' && uint.TryParse(accent.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

        return this with
        {
            Theme = Enum.IsDefined(Theme) ? Theme : LauncherTheme.Day,
            TitleBarMode = Enum.IsDefined(TitleBarMode) ? TitleBarMode : TitleBarMode.Default,
            TitleBarText = titleText[..Math.Min(titleText.Length, 40)],
            VersionListSource = Enum.IsDefined(VersionListSource) ? VersionListSource : VersionListSource.OfficialFirst,
            DownloadMaxConnections = Math.Clamp(DownloadMaxConnections, 1, 256),
            DownloadSpeedLimitMiBPerSecond = double.IsFinite(DownloadSpeedLimitMiBPerSecond) ? Math.Clamp(DownloadSpeedLimitMiBPerSecond, 0, 1024) : 0,
            AccentColor = validAccent ? accent.ToUpperInvariant() : "#80CEFF",
            WindowOpacity = double.IsFinite(WindowOpacity) ? Math.Clamp(WindowOpacity, 0.40, 1.0) : 1.0
        };
    }
}
