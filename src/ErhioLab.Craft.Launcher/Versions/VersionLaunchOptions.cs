using System.Text.Json;
using System.Text.Json.Serialization;

using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Versions;

/// <summary>仅保存版本明确覆盖的启动选项; null 始终从当前全局设置继承.</summary>
internal sealed record VersionLaunchOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseSpecifiedJava { get; init; }

    public string? JavaPath { get; init; }
    public GameProcessPriority? GamePriority { get; init; }
    public GameWindowMode? WindowMode { get; init; }
    public int? GameWindowWidth { get; init; }
    public int? GameWindowHeight { get; init; }
    public string? GameWindowTitle { get; init; }
    public bool? AutoAllocateMemory { get; init; }
    public int? CustomMemoryPercent { get; init; }
    public bool? OptimizeMemoryBeforeLaunch { get; init; }
    public string? JvmArguments { get; init; }
    public string? GameArguments { get; init; }
    public string? PreLaunchCommand { get; init; }
    public bool? WaitForPreLaunchCommand { get; init; }
    public GameGarbageCollector? GarbageCollector { get; init; }
    public bool? DisableJavaLaunchWrapper { get; init; }
    public bool? DisableLwjglUnsafeAgent { get; init; }
    public bool? PreferHighPerformanceGpu { get; init; }
    public bool? VerifyMicrosoftCertificates { get; init; }
    public bool? SkipFileVerification { get; init; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }

    internal VersionLaunchOptions Normalize() => this with
    {
        JavaPath = !UseSpecifiedJava || string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath.Trim(),
        GamePriority = ValidEnum(GamePriority),
        WindowMode = ValidEnum(WindowMode),
        GarbageCollector = ValidEnum(GarbageCollector),
        GameWindowWidth = Clamp(GameWindowWidth, 1, 16384),
        GameWindowHeight = Clamp(GameWindowHeight, 1, 16384),
        CustomMemoryPercent = Clamp(CustomMemoryPercent, 0, 100),
        GameWindowTitle = CleanText(GameWindowTitle, 512)?.Replace('\r', ' ').Replace('\n', ' '),
        JvmArguments = CleanText(JvmArguments, 16384),
        GameArguments = CleanText(GameArguments, 16384),
        PreLaunchCommand = CleanText(PreLaunchCommand, 8192)
    };

    // 只保存与当前全局值不同的项目; 空文本也是有效的独立值.
    internal VersionLaunchOptions WithSettings(LauncherSettings settings, LauncherSettings global)
    {
        var value = settings.Normalize();
        var baseline = global.Normalize();
        return (this with
        {
            GamePriority = Difference(value.GamePriority, baseline.GamePriority),
            WindowMode = Difference(value.WindowMode, baseline.WindowMode),
            GameWindowWidth = Difference(value.GameWindowWidth, baseline.GameWindowWidth),
            GameWindowHeight = Difference(value.GameWindowHeight, baseline.GameWindowHeight),
            GameWindowTitle = Difference(value.GameWindowTitle, baseline.GameWindowTitle),
            AutoAllocateMemory = Difference(value.AutoAllocateMemory, baseline.AutoAllocateMemory),
            CustomMemoryPercent = Difference(value.CustomMemoryPercent, baseline.CustomMemoryPercent),
            OptimizeMemoryBeforeLaunch = Difference(value.OptimizeMemoryBeforeLaunch, baseline.OptimizeMemoryBeforeLaunch),
            JvmArguments = Difference(value.JvmArguments, baseline.JvmArguments),
            GameArguments = Difference(value.GameArguments, baseline.GameArguments),
            PreLaunchCommand = Difference(value.PreLaunchCommand, baseline.PreLaunchCommand),
            WaitForPreLaunchCommand = Difference(value.WaitForPreLaunchCommand, baseline.WaitForPreLaunchCommand),
            GarbageCollector = Difference(value.GarbageCollector, baseline.GarbageCollector),
            DisableJavaLaunchWrapper = Difference(value.DisableJavaLaunchWrapper, baseline.DisableJavaLaunchWrapper),
            DisableLwjglUnsafeAgent = Difference(value.DisableLwjglUnsafeAgent, baseline.DisableLwjglUnsafeAgent),
            PreferHighPerformanceGpu = Difference(value.PreferHighPerformanceGpu, baseline.PreferHighPerformanceGpu),
            VerifyMicrosoftCertificates = Difference(value.VerifyMicrosoftCertificates, baseline.VerifyMicrosoftCertificates),
            SkipFileVerification = Difference(value.SkipFileVerification, baseline.SkipFileVerification)
        }).Normalize();
    }

    internal bool HasOverrides => this with { AdditionalData = null } != new VersionLaunchOptions() || AdditionalData is { Count: > 0 };

    internal LauncherSettings Apply(LauncherSettings global)
    {
        var local = Normalize();
        return (global with
        {
            GamePriority = local.GamePriority ?? global.GamePriority,
            WindowMode = local.WindowMode ?? global.WindowMode,
            GameWindowWidth = local.GameWindowWidth ?? global.GameWindowWidth,
            GameWindowHeight = local.GameWindowHeight ?? global.GameWindowHeight,
            GameWindowTitle = local.GameWindowTitle ?? global.GameWindowTitle,
            AutoAllocateMemory = local.AutoAllocateMemory ?? global.AutoAllocateMemory,
            CustomMemoryPercent = local.CustomMemoryPercent ?? global.CustomMemoryPercent,
            OptimizeMemoryBeforeLaunch = local.OptimizeMemoryBeforeLaunch ?? global.OptimizeMemoryBeforeLaunch,
            JvmArguments = local.JvmArguments ?? global.JvmArguments,
            GameArguments = local.GameArguments ?? global.GameArguments,
            PreLaunchCommand = local.PreLaunchCommand ?? global.PreLaunchCommand,
            WaitForPreLaunchCommand = local.WaitForPreLaunchCommand ?? global.WaitForPreLaunchCommand,
            GarbageCollector = local.GarbageCollector ?? global.GarbageCollector,
            DisableJavaLaunchWrapper = local.DisableJavaLaunchWrapper ?? global.DisableJavaLaunchWrapper,
            DisableLwjglUnsafeAgent = local.DisableLwjglUnsafeAgent ?? global.DisableLwjglUnsafeAgent,
            PreferHighPerformanceGpu = local.PreferHighPerformanceGpu ?? global.PreferHighPerformanceGpu,
            VerifyMicrosoftCertificates = local.VerifyMicrosoftCertificates ?? global.VerifyMicrosoftCertificates,
            SkipFileVerification = local.SkipFileVerification ?? global.SkipFileVerification
        }).Normalize();
    }

    private static T? ValidEnum<T>(T? value) where T : struct, Enum => value is { } actual && Enum.IsDefined(actual) ? actual : null;
    private static T? Difference<T>(T value, T baseline) where T : struct => EqualityComparer<T>.Default.Equals(value, baseline) ? null : value;
    private static string? Difference(string value, string baseline) => string.Equals(value, baseline, StringComparison.Ordinal) ? null : value;
    private static int? Clamp(int? value, int minimum, int maximum) => value is { } number ? Math.Clamp(number, minimum, maximum) : null;

    private static string? CleanText(string? value, int limit)
    {
        if (value is null) return null;
        var text = value.Replace("\0", "", StringComparison.Ordinal);
        return text[..Math.Min(text.Length, limit)];
    }
}
