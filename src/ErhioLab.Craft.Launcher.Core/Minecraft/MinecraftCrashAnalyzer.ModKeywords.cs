using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>用于区分日志明确给出的标识与堆栈推断出的搜索线索.</summary>
public enum MinecraftCrashModKeywordKind
{
    ModId,
    MixinConfig,
    JarFile,
    StackPackage
}

/// <summary>一条可用于搜索的线索, 不表示它就是崩溃原因.</summary>
public sealed record MinecraftCrashModKeyword(string Value, MinecraftCrashModKeywordKind Kind);

public static partial class MinecraftCrashAnalyzer
{
    private static readonly string[] SharedPackages =
    [
        "java.", "javax.", "jdk.", "sun.", "com.sun.", "net.minecraft.", "com.mojang.",
        "net.fabricmc.", "org.quiltmc.loader.", "net.minecraftforge.", "net.neoforged.", "cpw.mods.", "com.mumfrey.liteloader.",
        "org.spongepowered.asm.", "org.lwjgl.", "org.objectweb.asm.", "org.apache.", "org.slf4j.", "com.google.",
        "com.electronwill.nightconfig.", "io.netty.", "it.unimi.dsi.", "kotlin.", "kotlinx.", "scala.", "org.joml."
    ];

    /// <summary>从本次游戏的错误区段提取可搜索的 Mods 和堆栈线索.</summary>
    /// <param name="log">单次结束的 Minecraft 进程输出或崩溃文本.</param>
    /// <returns>优先选择日志明确给出的标识; 如果没有才列出包名. 最多六项, 未找到时返回空列表.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> 为 null.</exception>
    public static IReadOnlyList<MinecraftCrashModKeyword> FindModKeywords(string log)
    {
        ArgumentNullException.ThrowIfNull(log);
        ReadOnlySpan<char> tail = log.AsSpan();
        if (tail.Length > MaxCharacters)
        {
            tail = tail[^MaxCharacters..];
            int firstBreak = tail.IndexOf('\n');
            if (firstBreak < 0) return [];
            tail = tail[(firstBreak + 1)..];
        }

        var explicitKeywords = new List<MinecraftCrashModKeyword>();
        var stackKeywords = new List<MinecraftCrashModKeyword>();
        int errorBudget = 0;
        bool? xmlError = null;
        foreach (var range in tail.Split('\n'))
        {
            var line = Unwrap(tail[range].Trim());
            if (line.StartsWith("<log4j:Event ", StringComparison.Ordinal))
            {
                xmlError = Contains(line, "level=\"ERROR\"") || Contains(line, "level=\"FATAL\"");
                errorBudget = 0;
                continue;
            }

            if (line.StartsWith("</log4j:Event>", StringComparison.Ordinal))
            {
                xmlError = null;
                errorBudget = 0;
                continue;
            }

            if (line.IsEmpty || line.Length > 4096 || xmlError == false) continue;
            if (IsNonError(line) || line.Equals("-- System Details --", StringComparison.Ordinal))
            {
                errorBudget = 0;
                continue;
            }

            if (Contains(line, "/ERROR]") || Contains(line, "[ERROR]") || Contains(line, "/FATAL]") || Contains(line, "[FATAL]")
                || line.StartsWith("Caused by:", StringComparison.Ordinal) || line.StartsWith("Exception in thread ", StringComparison.Ordinal)
                || line.StartsWith("---- Minecraft Crash Report ----", StringComparison.Ordinal)
                || Contains(line, "Incompatible mods found!") || Contains(line, "Missing or unsupported mandatory dependencies:")
                || Contains(line, "Mixin apply for mod "))
                errorBudget = 120;
            if (xmlError != true && errorBudget == 0) continue;
            if (errorBudget > 0) errorBudget--;

            if (line.StartsWith("at ", StringComparison.Ordinal))
            {
                if (ReadStackPackage(line) is { } package) Remember(stackKeywords, package, MinecraftCrashModKeywordKind.StackPackage);
                continue;
            }

            // 显式字段和加载器消息只接受 ASCII 标识, 不使用原始异常文本作关键词.
            string text = line.ToString();
            foreach (Match match in ModIdPattern().Matches(text)) RememberModId(explicitKeywords, match.Groups[1].Value);
            foreach (Match match in DependencyPattern().Matches(text)) RememberModId(explicitKeywords, match.Groups[1].Value);
            foreach (var match in MixinPattern().EnumerateMatches(line))
                Remember(explicitKeywords, line.Slice(match.Index, match.Length).ToString(), MinecraftCrashModKeywordKind.MixinConfig);

            const string fileField = "Mod File:";
            if (line.StartsWith(fileField, StringComparison.OrdinalIgnoreCase))
            {
                var name = line[fileField.Length..].Trim().Trim("'\"");
                int slash = name.LastIndexOfAny('/', '\\');
                name = name[(slash + 1)..];
                if (name.Length is > 4 and <= 120 && name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                                  && IsSafeFileName(name)) Remember(explicitKeywords, name.ToString(), MinecraftCrashModKeywordKind.JarFile);
            }
        }

        return explicitKeywords.Count > 0
            ? explicitKeywords.OrderBy(keyword => keyword.Kind).Take(6).ToArray()
            : stackKeywords.Take(6).ToArray();
    }

    private static string? ReadStackPackage(ReadOnlySpan<char> line)
    {
        int open = line.IndexOf('(');
        if (open < 4) return null;
        var frame = line[3..open];
        frame = frame[(frame.LastIndexOf('/') + 1)..];
        int method = frame.LastIndexOf('.');
        if (method < 0) return null;
        int type = frame[..method].LastIndexOf('.');
        if (type <= 0) return null;
        var package = frame[..type];
        if (package.Length > 120 || !package.Contains('.') || !IsSafeFileName(package) || package.Contains('-')) return null;
        foreach (string prefix in SharedPackages)
            if (package.StartsWith(prefix, StringComparison.Ordinal) || package.Equals(prefix.AsSpan(0, prefix.Length - 1), StringComparison.Ordinal))
                return null;
        return package.ToString();
    }

    private static bool IsSafeFileName(ReadOnlySpan<char> name)
    {
        foreach (char ch in name)
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-' or '+'))
                return false;
        return true;
    }

    private static void RememberModId(List<MinecraftCrashModKeyword> list, string id)
    {
        id = id.ToLowerInvariant();
        if (id is not ("minecraft" or "java" or "forge" or "neoforge" or "fabricloader" or "quilt_loader"))
            Remember(list, id, MinecraftCrashModKeywordKind.ModId);
    }

    private static void Remember(List<MinecraftCrashModKeyword> list, string value, MinecraftCrashModKeywordKind kind)
    {
        if (list.Any(item => item.Kind == kind && item.Value.Equals(value, StringComparison.OrdinalIgnoreCase))) return;
        // 保留每种来源的近期线索, 避免长堆栈挤掉优先级更高的标识.
        if (list.Count(item => item.Kind == kind) >= 6) list.RemoveAt(list.FindLastIndex(item => item.Kind == kind));
        list.Insert(0, new(value, kind));
    }

    [GeneratedRegex("(?:\\b(?:Mod ID|Requested by):\\s*['\"]?|\\bfrom mod\\s+|\\bMixin apply for mod\\s+)([a-z][a-z0-9_-]{1,63})(?![a-z0-9_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModIdPattern();

    [GeneratedRegex("\\bmod\\s+'[^'\r\n]{1,120}'\\s*\\(([a-z][a-z0-9_-]{1,63})\\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DependencyPattern();

    [GeneratedRegex("(?<![a-z0-9_.-])(?:[a-z0-9_-]{1,60}[.-])?mixins?(?:[.-][a-z0-9_-]{1,32}){0,3}\\.json(?![a-z0-9_.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MixinPattern();
}
