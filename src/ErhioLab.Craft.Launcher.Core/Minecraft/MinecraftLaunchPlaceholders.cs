using System.Globalization;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>替换窗口标题/参数和命令中的标记; 只处理文本, 不执行命令或启动进程.</summary>
public static partial class MinecraftLaunchPlaceholders
{
    /// <summary>按固定顺序替换普通标记, 再处理设置/变量和 Minecraft 原生参数标记.</summary>
    public static string Expand(string template, MinecraftLaunchPlaceholderContext context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);
        string result = template;
        foreach ((string name, string? value) in context.AdditionalValues.Concat(CommonValues(context)))
        {
            result = CommonTokenPattern().Replace(result, match => match.Groups["name"].Value == name && !match.Groups["argument"].Success ? value ?? "" : match.Value);
        }

        result = CommonTokenPattern().Replace(result, match =>
        {
            string argument = match.Groups["argument"].Value;
            if (match.Groups["name"].Value == "setup" && match.Groups["argument"].Success) return context.Settings.TryGetValue(argument, out string? setting) ? setting ?? "" : match.Value;
            return match.Value;
        });
        result = CommonTokenPattern().Replace(result, match =>
        {
            string argument = match.Groups["argument"].Value;
            if (match.Groups["name"].Value != "variable" || !match.Groups["argument"].Success) return match.Value;
            int separator = argument.IndexOf(':');
            string name = separator < 0 ? argument : argument[..separator];
            string fallback = separator < 0 ? "" : argument[(separator + 1)..];
            return context.Variables.TryGetValue(name, out string? variable) ? variable ?? "" : fallback;
        });

        Dictionary<string, string?> native = new(StringComparer.Ordinal)
        {
            ["auth_player_name"] = context.PlayerName,
            ["auth_uuid"] = context.PlayerUuid?.ToString("N"),
            ["version_name"] = context.VersionName,
            ["game_directory"] = context.GameInstanceDirectory ?? context.GameDirectory,
            ["library_directory"] = Combine(context.GameDirectory, "libraries"),
            ["assets_root"] = Combine(context.GameDirectory, "assets")
        };
        return NativeTokenPattern().Replace(result, match =>
        {
            string name = match.Groups["name"].Value;
            if (context.NativeValues.TryGetValue(name, out string? value)) return value ?? "";
            return native.TryGetValue(name, out value) ? value ?? "" : match.Value;
        });
    }

    /// <summary>列出结果中仍未替换的标记(含花括号), 供调用方提示拼写错误或缺失的上下文.</summary>
    public static IReadOnlyList<string> GetUnresolvedTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AnyTokenPattern().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<KeyValuePair<string, string?>> CommonValues(MinecraftLaunchPlaceholderContext context)
    {
        (string Name, string? Value)[] values =
        [
            ("launcher_name", context.LauncherName),
            ("launcher_version", context.LauncherVersion),
            ("path", Directory(context.LauncherDirectory)),
            ("path_with_name", context.LauncherExecutablePath),
            ("path_temp", Directory(context.CacheDirectory)),
            ("date", context.Timestamp.ToString("yyyy/M/d", CultureInfo.InvariantCulture)),
            ("time", context.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            ("java", Directory(context.JavaDirectory)),
            ("minecraft", Directory(context.GameDirectory)),
            ("version_path", Directory(context.VersionDirectory)),
            ("verpath", Directory(context.VersionDirectory)),
            ("version_indie", Directory(context.GameInstanceDirectory ?? context.GameDirectory)),
            ("verindie", Directory(context.GameInstanceDirectory ?? context.GameDirectory)),
            ("name", context.VersionName),
            ("version", context.GameVersion),
            ("forge", context.ForgeVersion),
            ("fabric", context.FabricVersion),
            ("neoforge", context.NeoForgeVersion),
            ("quilt", context.QuiltVersion),
            ("liteloader", context.HasLiteLoader ? "true" : ""),
            ("user", context.PlayerName),
            ("uuid", context.PlayerUuid?.ToString("N")),
            ("login", context.LoginType)
        ];
        return values.Select(value => new KeyValuePair<string, string?>(value.Name, value.Value));
    }

    private static string Directory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (path.EndsWith('/') || path.EndsWith('\\')) return path;
        return path + (path.Contains('\\') || path.Length > 1 && path[1] == ':' ? "\\" : "/");
    }

    private static string Combine(string? directory, string name) => string.IsNullOrEmpty(directory) ? "" : Directory(directory) + name;

    [GeneratedRegex(@"(?<!\$)\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::(?<argument>[^{}\r\n]*))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex CommonTokenPattern();

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex NativeTokenPattern();

    [GeneratedRegex(@"\$?\{[A-Za-z_][A-Za-z0-9_]*(?::[^{}\r\n]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex AnyTokenPattern();
}
