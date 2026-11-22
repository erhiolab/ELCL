using System.Globalization;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>能从单次游戏日志确认的故障特征, 不直接归责于某个 Mods 或驱动.</summary>
public enum MinecraftCrashKind
{
    JavaTooOld,
    InvalidJvmOption,
    HeapReservationFailed,
    JavaHeapExhausted,
    NativeMemoryExhausted,
    OpenGlUnavailable,
    ModDependencyConflict,
    MixinFailed,
    NativeJvmCrash,
    ManualDebugCrash
}

/// <summary>本地日志规则的匹配结果, 不携带原始日志行.</summary>
public sealed record MinecraftCrashDiagnosis(MinecraftCrashKind Kind, int? RequiredJavaMajorVersion = null);

/// <summary>根据本次运行的文本日志识别 Minecraft 和 JVM 的已知错误特征.</summary>
public static partial class MinecraftCrashAnalyzer
{
    private const int MaxCharacters = 512 * 1024;

    /// <summary>从日志末尾起查找明确的错误特征; 找不到时不推测原因.</summary>
    /// <param name="log">已经退出的单次 Minecraft 进程输出或文本日志.</param>
    /// <returns>最靠近退出时的已知故障, 没有明确匹配时为 null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> 为 null.</exception>
    public static MinecraftCrashDiagnosis? Analyze(string log)
    {
        ArgumentNullException.ThrowIfNull(log);
        ReadOnlySpan<char> remaining = log.AsSpan(Math.Max(0, log.Length - MaxCharacters));
        MinecraftCrashDiagnosis? fallback = null;
        while (!remaining.IsEmpty)
        {
            int newline = remaining.LastIndexOf('\n');
            ReadOnlySpan<char> line = Unwrap(remaining[(newline + 1)..].Trim());
            remaining = newline < 0 ? [] : remaining[..newline];
            if (line.IsEmpty || IsNonError(line)) continue;

            if (IsManualDebugCrash(line)) return new(MinecraftCrashKind.ManualDebugCrash);
            if (Contains(line, "java.lang.UnsupportedClassVersionError") && IsException(line) && (Contains(line, "more recent version of the Java Runtime") || Contains(line, "only recognizes class file versions up to") || Contains(line, "Unsupported major.minor version")))
                return new(MinecraftCrashKind.JavaTooOld, ReadRequiredJava(line));
            if (line.StartsWith("Unrecognized VM option", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Improperly specified VM option", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Invalid maximum heap size:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Invalid initial heap size:", StringComparison.OrdinalIgnoreCase))
                return new(MinecraftCrashKind.InvalidJvmOption);
            if (Contains(line, "Could not reserve enough space") && Contains(line, "object heap"))
                return new(MinecraftCrashKind.HeapReservationFailed);
            if (Contains(line, "java.lang.OutOfMemoryError") && IsException(line) && (Contains(line, "Java heap space") || Contains(line, "GC overhead limit exceeded")))
                return new(MinecraftCrashKind.JavaHeapExhausted);
            if (line.StartsWith("# Native memory allocation", StringComparison.OrdinalIgnoreCase) && Contains(line, "failed") || line.StartsWith("# There is insufficient memory for the Java Runtime Environment to continue", StringComparison.OrdinalIgnoreCase))
                return new(MinecraftCrashKind.NativeMemoryExhausted);
            if (Contains(line, "GLFW error 65542") && Contains(line, "WGL:") || Contains(line, "The driver does not appear to support OpenGL") || Contains(line, "LWJGLException: Pixel format not accelerated"))
                return new(MinecraftCrashKind.OpenGlUnavailable);
            if (Contains(line, "Incompatible mods found!") || Contains(line, "Mod resolution encountered an incompatible mod set!") || Contains(line, "Missing or unsupported mandatory dependencies:"))
                return new(MinecraftCrashKind.ModDependencyConflict);

            // Mixin 和 JVM 的外层错误不应盖住日志前面更明确的兼容性或资源错误.
            if ((Contains(line, "org.spongepowered.asm.mixin.") && IsException(line) && (Contains(line, "MixinApplyError") || Contains(line, "InvalidMixinException") || Contains(line, "InvalidInjectionException"))) || (Contains(line, "Mixin apply for mod ") && Contains(line, " failed")))
                fallback ??= new(MinecraftCrashKind.MixinFailed);
            else if (line.StartsWith("# A fatal error has been detected by the Java Runtime Environment", StringComparison.OrdinalIgnoreCase))
                fallback ??= new(MinecraftCrashKind.NativeJvmCrash);
        }

        return fallback;
    }

    private static bool IsNonError(ReadOnlySpan<char> line) => Contains(line, "[CHAT]") || Contains(line, "/WARN]") || Contains(line, "[WARN]") || Contains(line, "level=\"WARN\"") || Contains(line, "/INFO]") || Contains(line, "[INFO]");

    private static bool IsException(ReadOnlySpan<char> line) => line.StartsWith("java.lang.", StringComparison.Ordinal) || line.StartsWith("org.spongepowered.asm.mixin.", StringComparison.Ordinal) || Contains(line, "Caused by:") || Contains(line, "Exception in thread") || Contains(line, "/ERROR]") || Contains(line, "[ERROR]");

    // 只接受已抛出的异常, 长按时显示的聊天预警不表示游戏真的因此退出.
    private static bool IsManualDebugCrash(ReadOnlySpan<char> line)
    {
        const string logPrefixEnd = "/ERROR]: ";
        int prefix = line.StartsWith("[", StringComparison.Ordinal) ? line.IndexOf(logPrefixEnd, StringComparison.Ordinal) : -1;
        if (prefix >= 0) line = line[(prefix + logPrefixEnd.Length)..].TrimStart();
        return line.Equals("net.minecraft.ReportedException: Manually triggered debug crash", StringComparison.Ordinal)
               || line.Equals("Caused by: java.lang.Throwable: Manually triggered debug crash", StringComparison.Ordinal)
               || line.Equals("java.lang.Throwable: Manually triggered debug crash", StringComparison.Ordinal);
    }

    private static ReadOnlySpan<char> Unwrap(ReadOnlySpan<char> line)
    {
        if (line.Length > 25 && line[4] == '-' && line[7] == '-' && line[10] == 'T')
        {
            int channel = line.IndexOf(" [OUT] ", StringComparison.Ordinal);
            if (channel < 0) channel = line.IndexOf(" [ERR] ", StringComparison.Ordinal);
            if (channel is >= 19 and <= 36) line = line[(channel + 7)..].Trim();
        }

        const string xmlMessage = "<log4j:Message><![CDATA[";
        const string xmlThrowable = "<log4j:Throwable><![CDATA[";
        if (line.StartsWith(xmlMessage, StringComparison.Ordinal)) line = line[xmlMessage.Length..];
        else if (line.StartsWith(xmlThrowable, StringComparison.Ordinal)) line = line[xmlThrowable.Length..];
        else return line;
        int closing = line.LastIndexOf("]]></log4j:", StringComparison.Ordinal);
        return closing < 0 ? line.TrimStart() : line[..closing].Trim();
    }

    private static int? ReadRequiredJava(ReadOnlySpan<char> line)
    {
        string marker = "class file version ";
        int start = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            marker = "unsupported major.minor version ";
            start = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        }

        if (start < 0) return null;
        var version = line[(start + marker.Length)..];
        int digits = 0;
        while (digits < version.Length && char.IsAsciiDigit(version[digits])) digits++;
        return int.TryParse(version[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out int major) && major is >= 45 and <= 120
            ? major - 44
            : null;
    }

    private static bool Contains(ReadOnlySpan<char> value, string token) => value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
