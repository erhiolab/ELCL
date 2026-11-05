using System.Globalization;

using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>启动器的启动/外观和下载配置, 由客户端定义和校验.</summary>
public sealed record LauncherSettings
{
    /// <summary>当前明暗主题.</summary>
    public LauncherTheme Theme { get; init; } = LauncherTheme.Day;

    /// <summary>界面语言代码，对应 Languages 下的 JSON 文件名.</summary>
    public string Language { get; init; } = LocalizationManager.DefaultLanguage;

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

    /// <summary>按优先级排列的 Java 可执行文件列表.</summary>
    public IReadOnlyList<JavaInstallation> JavaInstallations { get; init; } = [];

    /// <summary>用户主动移除的路径, 自动刷新时不会重新加入.</summary>
    public IReadOnlyList<string> IgnoredJavaPaths { get; init; } = [];

    /// <summary>版本选择页显示的游戏文件夹, 顺序即列表顺序.</summary>
    public IReadOnlyList<GameFolderSetting> GameFolders { get; init; } = [];

    /// <summary>当前查看的游戏文件夹路径.</summary>
    public string? SelectedGameFolderPath { get; init; }

    /// <summary>当前选中的本地游戏版本目录.</summary>
    public string? SelectedVersionPath { get; init; }

    /// <summary>当前选中版本的 JSON 标识符, 用于启动页展示.</summary>
    public string? SelectedVersionId { get; init; }

    /// <summary>将外部配置值限制在启动器支持的范围内.</summary>
    /// <returns>规范化后的设置.</returns>
    public LauncherSettings Normalize()
    {
        string accent = NormalizeAccent(AccentColor);
        string titleText = TitleBarText ?? string.Empty;
        bool validAccent = accent.Length == 7 && accent[0] == '#' && uint.TryParse(accent.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

        return this with
        {
            Theme = Enum.IsDefined(Theme) ? Theme : LauncherTheme.Day,
            Language = LocalizationManager.IsAvailable(Language) ? Language : LocalizationManager.DefaultLanguage,
            TitleBarMode = Enum.IsDefined(TitleBarMode) ? TitleBarMode : TitleBarMode.Default,
            TitleBarText = titleText[..Math.Min(titleText.Length, 40)],
            VersionListSource = Enum.IsDefined(VersionListSource) ? VersionListSource : VersionListSource.OfficialFirst,
            DownloadMaxConnections = Math.Clamp(DownloadMaxConnections, 1, 256),
            DownloadSpeedLimitMiBPerSecond = double.IsFinite(DownloadSpeedLimitMiBPerSecond) ? Math.Clamp(DownloadSpeedLimitMiBPerSecond, 0, 1024) : 0,
            AccentColor = validAccent ? accent.ToUpperInvariant() : "#80CEFF",
            WindowOpacity = double.IsFinite(WindowOpacity) ? Math.Clamp(WindowOpacity, 0.40, 1.0) : 1.0,
            JavaInstallations = ValidJavaInstallations(JavaInstallations).DistinctBy(java => java.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            IgnoredJavaPaths = OrEmpty(IgnoredJavaPaths).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            GameFolders = ValidGameFolders(GameFolders).DistinctBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            SelectedGameFolderPath = NormalizePath(SelectedGameFolderPath),
            SelectedVersionPath = NormalizePath(SelectedVersionPath),
            SelectedVersionId = string.IsNullOrWhiteSpace(SelectedVersionId) ? null : SelectedVersionId.Trim()[..Math.Min(SelectedVersionId.Trim().Length, 128)]
        };
    }

    private static IEnumerable<T> OrEmpty<T>(IEnumerable<T>? values) => values ?? [];

    private static string NormalizeAccent(string? color) => color?.Trim() ?? string.Empty;

    private static IEnumerable<JavaInstallation> ValidJavaInstallations(IEnumerable<JavaInstallation?>? values) => values?.Where(java => java is not null && !string.IsNullOrWhiteSpace(java.Path)).Select(java => java!) ?? [];

    private static IEnumerable<GameFolderSetting> ValidGameFolders(IEnumerable<GameFolderSetting?>? values)
    {
        foreach (GameFolderSetting? folder in values ?? [])
        {
            string? path = NormalizePath(folder?.Path);
            if (path is null)
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(folder!.Name)
                ? System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar))
                : folder.Name.Trim();
            if (name.Length == 0)
            {
                name = "Minecraft";
            }

            string? nameKey = folder.NameKey is "folder.current" or "folder.official" ? folder.NameKey : null;
            if (nameKey is null && !folder.IsNameCustomized)
            {
                string? defaultKey = SamePath(path, System.IO.Path.Combine(AppContext.BaseDirectory, ".minecraft"))
                    ? "folder.current"
                    : SamePath(path, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"))
                        ? "folder.official"
                        : null;
                if (defaultKey is not null && LocalizationManager.AvailableLanguages.Any(language =>
                        name == LocalizationManager.GetForLanguage(language, defaultKey)))
                    nameKey = defaultKey;
            }

            yield return new GameFolderSetting(path, name[..Math.Min(name.Length, 64)], nameKey, folder.IsNameCustomized);
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(
        left.TrimEnd(System.IO.Path.DirectorySeparatorChar),
        right.TrimEnd(System.IO.Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.IO.Path.IsPathFullyQualified(value))
        {
            return null;
        }

        try
        {
            return System.IO.Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return null;
        }
    }
}
