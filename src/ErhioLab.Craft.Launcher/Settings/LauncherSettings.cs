using System.Globalization;

using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>启动器的启动/外观和下载配置, 由客户端定义和校验.</summary>
public sealed record LauncherSettings
{
    public const string DefaultJvmArguments = "-XX:-OmitStackTraceInFastThrow -Djdk.lang.Process.allowAmbiguousCommands=True -Dfml.ignoreInvalidMinecraftCertificates=True -Dfml.ignorePatchDiscrepancies=True";
    /// <summary>当前明暗主题.</summary>
    public LauncherTheme Theme { get; init; } = LauncherTheme.Day;

    /// <summary>界面语言代码, 对应 Languages 下的 JSON 文件名.</summary>
    public string Language { get; init; } = LocalizationManager.DefaultLanguage;

    /// <summary>允许发送匿名启动事件, 使用随机安装 ID, 默认开启.</summary>
    public bool AnonymousAnalyticsEnabled { get; init; } = true;

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

    /// <summary>游戏文件下载的优先来源, 另一来源在失败时回退.</summary>
    public VersionListSource FileDownloadSource { get; init; } = VersionListSource.OfficialFirst;

    /// <summary>单个下载任务最多同时使用的连接数, 范围为 1 到 256.</summary>
    public int DownloadMaxConnections { get; init; } = 8;
    /// <summary>是否已触发零下载线程彩蛋, 触发后滑块最低为 1.</summary>
    public bool ZeroDownloadThreadsEggShown { get; init; }

    /// <summary>下载任务的速度上限, 单位为 MiB/s; 0 表示不限速.</summary>
    public double DownloadSpeedLimitMiBPerSecond { get; init; }

    /// <summary>游戏进程优先级.</summary>
    public GameProcessPriority GamePriority { get; init; } = GameProcessPriority.Normal;

    /// <summary>游戏窗口显示方式.</summary>
    public GameWindowMode WindowMode { get; init; } = GameWindowMode.Default;

    /// <summary>自定义游戏窗口宽度, 单位为像素.</summary>
    public int GameWindowWidth { get; init; } = 854;

    /// <summary>自定义游戏窗口高度, 单位为像素.</summary>
    public int GameWindowHeight { get; init; } = 480;

    /// <summary>游戏窗口标题模板; 空字符串使用游戏自己的标题.</summary>
    public string GameWindowTitle { get; init; } = "";

    /// <summary>附加 JVM 参数, 可包含换行和替换标记.</summary>
    public string JvmArguments { get; init; } = DefaultJvmArguments;

    /// <summary>附加游戏参数模板.</summary>
    public string GameArguments { get; init; } = "";

    /// <summary>游戏启动前执行的 Windows 命令模板; 在创建游戏进程前执行.</summary>
    public string PreLaunchCommand { get; init; } = "";

    public bool WaitForPreLaunchCommand { get; init; } = true;
    public GameGarbageCollector GarbageCollector { get; init; } = GameGarbageCollector.StandardG1;
    public bool DisableJavaLaunchWrapper { get; init; }
    public bool DisableLwjglUnsafeAgent { get; init; }
    public bool PreferHighPerformanceGpu { get; init; } = true;
    /// <summary>证书验证偏好; 仅应用于启动前账户请求, 不影响下载及皮肤客户端.</summary>
    public bool VerifyMicrosoftCertificates { get; init; } = true;

    /// <summary>启动时仅检查文件是否存在, 不校验已有文件的大小或 SHA-1; 手动补全不受影响.</summary>
    public bool SkipFileVerification { get; init; }

    /// <summary>是否根据可用物理内存自动估算游戏内存.</summary>
    public bool AutoAllocateMemory { get; init; } = true;

    /// <summary>自定义模式使用当前可用物理内存的百分比, 范围 0 到 100.</summary>
    public int CustomMemoryPercent { get; init; } = 50;

    /// <summary>内存容量的显示与自动分配换算标准.</summary>
    public MemoryUnitSystem MemoryUnit { get; init; } = MemoryUnitSystem.Binary;

    /// <summary>在选择 Java 与计算游戏内存前整理 Windows 工作集; 默认关闭.</summary>
    public bool OptimizeMemoryBeforeLaunch { get; init; }

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

    public bool DailyLuckDisclaimerAccepted { get; init; }
    /// <summary>最近一次抽取人品文案的本地日期与语言键, 保存在配置中.</summary>
    public DateOnly? DailyLuckDate { get; init; }
    public string? DailyLuckMessageKey { get; init; }
    public int PerformanceRefreshMilliseconds { get; init; } = 1000;

    /// <summary>将外部配置值限制在启动器支持的范围内.</summary>
    /// <returns>规范化后的设置.</returns>
    public LauncherSettings Normalize()
    {
        string accent = NormalizeAccent(AccentColor);
        string titleText = TitleBarText ?? string.Empty;
        bool validAccent = accent.Length == 7 && accent[0] == '#' && uint.TryParse(accent.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        string? selectedFolderPath = NormalizePath(SelectedGameFolderPath);
        string? selectedVersionPath = NormalizePath(SelectedVersionPath);
        GameFolderSetting[] gameFolders = ValidGameFolders(GameFolders).DistinctBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        // 旧配置只在顶层保存当前版本; 首次读取时把它归入对应文件夹.
        if (selectedFolderPath is not null && selectedVersionPath is not null)
        {
            gameFolders = gameFolders.Select(folder =>
                folder.LastSelectedVersionPath is null && SamePath(folder.Path, selectedFolderPath) && IsVersionInFolder(selectedVersionPath, folder.Path)
                    ? folder with { LastSelectedVersionPath = selectedVersionPath }
                    : folder).ToArray();
        }

        return this with
        {
            Theme = Enum.IsDefined(Theme) ? Theme : LauncherTheme.Day,
            PerformanceRefreshMilliseconds = PerformanceRefreshMilliseconds is 500 or 1000 or 2000 or 5000 or 10000 ? PerformanceRefreshMilliseconds : 1000,
            Language = LocalizationManager.IsAvailable(Language) ? Language : LocalizationManager.DefaultLanguage,
            TitleBarMode = Enum.IsDefined(TitleBarMode) ? TitleBarMode : TitleBarMode.Default,
            TitleBarText = titleText[..Math.Min(titleText.Length, 40)],
            VersionListSource = Enum.IsDefined(VersionListSource) ? VersionListSource : VersionListSource.OfficialFirst,
            FileDownloadSource = Enum.IsDefined(FileDownloadSource) ? FileDownloadSource : VersionListSource.OfficialFirst,
            DownloadMaxConnections = Math.Clamp(DownloadMaxConnections, 1, 256),
            DownloadSpeedLimitMiBPerSecond = double.IsFinite(DownloadSpeedLimitMiBPerSecond) ? Math.Clamp(DownloadSpeedLimitMiBPerSecond, 0, 1024) : 0,
            GamePriority = Enum.IsDefined(GamePriority) ? GamePriority : GameProcessPriority.Normal,
            WindowMode = Enum.IsDefined(WindowMode) ? WindowMode : GameWindowMode.Default,
            GameWindowWidth = Math.Clamp(GameWindowWidth, 1, 16384),
            GameWindowHeight = Math.Clamp(GameWindowHeight, 1, 16384),
            GameWindowTitle = NormalizeLaunchText(GameWindowTitle, 512).Replace('\r', ' ').Replace('\n', ' '),
            JvmArguments = NormalizeLaunchText(JvmArguments, 16384, DefaultJvmArguments),
            GameArguments = NormalizeLaunchText(GameArguments, 16384),
            PreLaunchCommand = NormalizeLaunchText(PreLaunchCommand, 8192),
            GarbageCollector = Enum.IsDefined(GarbageCollector) ? GarbageCollector : GameGarbageCollector.StandardG1,
            CustomMemoryPercent = Math.Clamp(CustomMemoryPercent, 0, 100),
            MemoryUnit = Enum.IsDefined(MemoryUnit) ? MemoryUnit : MemoryUnitSystem.Binary,
            AccentColor = validAccent ? accent.ToUpperInvariant() : "#80CEFF",
            WindowOpacity = double.IsFinite(WindowOpacity) ? Math.Clamp(WindowOpacity, 0.40, 1.0) : 1.0,
            JavaInstallations = ValidJavaInstallations(JavaInstallations).DistinctBy(java => java.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            IgnoredJavaPaths = OrEmpty(IgnoredJavaPaths).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            GameFolders = gameFolders,
            SelectedGameFolderPath = selectedFolderPath,
            SelectedVersionPath = selectedVersionPath,
            SelectedVersionId = string.IsNullOrWhiteSpace(SelectedVersionId) ? null : SelectedVersionId.Trim()[..Math.Min(SelectedVersionId.Trim().Length, 128)]
        };
    }

    private static IEnumerable<T> OrEmpty<T>(IEnumerable<T>? values) => values ?? [];

    private static string NormalizeLaunchText(string? value, int limit, string fallback = "")
    {
        string text = (value ?? fallback).Replace("\0", "", StringComparison.Ordinal);
        return text[..Math.Min(text.Length, limit)];
    }

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

            string? lastVersionPath = NormalizePath(folder.LastSelectedVersionPath);
            if (lastVersionPath is not null && !IsVersionInFolder(lastVersionPath, path)) lastVersionPath = null;
            yield return new GameFolderSetting(path, name[..Math.Min(name.Length, 64)], nameKey, folder.IsNameCustomized, lastVersionPath);
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(
        left.TrimEnd(System.IO.Path.DirectorySeparatorChar),
        right.TrimEnd(System.IO.Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    private static bool IsVersionInFolder(string versionPath, string folderPath) =>
        System.IO.Path.GetDirectoryName(versionPath) is { } parent &&
        SamePath(parent, System.IO.Path.Combine(folderPath, "versions"));

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
