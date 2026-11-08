using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ErhioLab.Craft.Launcher.Core.Java;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public enum MinecraftGarbageCollector
{
    PreferZgc,
    PreferGenerationalZgc,
    StandardG1,
    TunedG1,
    Custom
}

/// <summary>调用方提供的启动输入; 不读取任何配置或账户文件. 令牌与完整参数不应写入日志.</summary>
public sealed class MinecraftLaunchOptions
{
    public required JavaInstallation Java { get; init; }
    public required MinecraftLaunchPlaceholderContext Context { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string NativesDirectory { get; init; }
    public required string AccessToken { get; init; }
    public bool IsMicrosoftAccount { get; init; }
    public string ClientId { get; init; } = "";
    public int MemoryMiB { get; init; } = 2048;
    public MinecraftGarbageCollector GarbageCollector { get; init; } = MinecraftGarbageCollector.StandardG1;
    public string JvmArguments { get; init; } = "";
    public string GameArguments { get; init; } = "";
    public bool Fullscreen { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }
    public string? JavaWrapperPath { get; init; }
    public string? LwjglAgentPath { get; init; }
    public MinecraftLaunchEnvironment Environment { get; init; } = MinecraftLaunchEnvironment.Current;
}

public static partial class MinecraftLaunchBuilder
{
    /// <summary>生成参数列表, 避免把包含空格的路径和用户名重新拼接为 shell 命令. 不启动进程.</summary>
    public static ProcessStartInfo CreateStartInfo(MinecraftLaunchVersion version, MinecraftLaunchFilePlan files, MinecraftLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MemoryMiB < 128) throw new ArgumentOutOfRangeException(nameof(options), "游戏内存不能少于 128 MiB.");
        using var document = JsonDocument.Parse(version.Json);
        var root = document.RootElement;
        var context = CreateContext(version, files, options);

        string Expand(string argument)
        {
            string result = MinecraftLaunchPlaceholders.Expand(argument, context);
            var unresolved = MinecraftLaunchPlaceholders.GetUnresolvedTokens(result);
            if (unresolved.Count > 0) throw new InvalidDataException("未提供启动参数标记: " + string.Join(", ", unresolved));
            return result;
        }

        var features = new Dictionary<string, bool> { ["has_custom_resolution"] = options.WindowWidth.HasValue && options.WindowHeight.HasValue };

        List<string> Arguments(string kind)
        {
            List<string> result = [];
            if (!root.TryGetProperty("arguments", out var all) || !all.TryGetProperty(kind, out var args)) return result;
            foreach (var item in args.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) result.Add(Expand(item.GetString()!));
                else if (MinecraftLaunchRules.Allows(item, options.Environment, features) && item.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.Array) result.AddRange(value.EnumerateArray().Select(element => Expand(element.GetString()!)));
                    else result.Add(Expand(value.GetString()!));
                }
            }

            return result;
        }

        List<string> jvm = Arguments("jvm");
        if (jvm.Count == 0) jvm.Add("-Djava.library.path=" + options.NativesDirectory);
        jvm.AddRange(SplitArguments(options.JvmArguments).Select(Expand));
        if (!jvm.Contains("-cp") && !jvm.Contains("-classpath") && !jvm.Contains("--class-path"))
            jvm.AddRange(["-cp", string.Join(options.Environment.OsName == "windows" ? ";" : ":", files.ClassPath)]);
        jvm.RemoveAll(value => value.StartsWith("-Xmx", StringComparison.Ordinal) || value.StartsWith("-Xms", StringComparison.Ordinal));
        jvm.Add("-Xmx" + options.MemoryMiB.ToString(CultureInfo.InvariantCulture) + "M");
        ApplyGarbageCollector(jvm, options);
        if (files.LoggingArgument is { } logging)
            jvm.Add(Expand(logging.Replace("${path}", files.LoggingPath, StringComparison.Ordinal)));
        jvm.Add("-Dlog4j2.formatMsgNoLookups=true");
        if (!jvm.Any(value => value.StartsWith("-Dfile.encoding=", StringComparison.Ordinal))) jvm.Add("-Dfile.encoding=UTF-8");
        jvm.Add("-Dsun.stdout.encoding=UTF-8");
        jvm.Add("-Dsun.stderr.encoding=UTF-8");
        if (options.Java.MajorVersion >= 19) jvm.AddRange(["-Dstdout.encoding=UTF-8", "-Dstderr.encoding=UTF-8"]);
        if (options.LwjglAgentPath is { } agent) jvm.Add("-javaagent:" + Path.GetFullPath(agent));
        jvm = DeduplicateJvm(jvm);

        List<string> game = [];
        if (root.TryGetProperty("minecraftArguments", out var oldArgs)) game.AddRange(SplitArguments(oldArgs.GetString()!).Select(Expand));
        game.AddRange(Arguments("game"));
        game.AddRange(SplitArguments(options.GameArguments).Select(Expand));
        if (options.Fullscreen) SetGameArgument(game, "--fullscreen", null);
        else
        {
            if (options.WindowWidth is { } width) SetGameArgument(game, "--width", width.ToString(CultureInfo.InvariantCulture));
            if (options.WindowHeight is { } height) SetGameArgument(game, "--height", height.ToString(CultureInfo.InvariantCulture));
        }

        game = DeduplicateGame(game);
        ProcessStartInfo start = new(options.Java.Path)
        {
            WorkingDirectory = Path.GetFullPath(options.WorkingDirectory), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in jvm) start.ArgumentList.Add(argument);
        if (options.JavaWrapperPath is { } wrapper)
        {
            if (options.Java.MajorVersion >= 9 && root.GetProperty("mainClass").GetString() == "cpw.mods.bootstraplauncher.BootstrapLauncher")
            {
                start.ArgumentList.Add("--add-exports");
                start.ArgumentList.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
            }

            start.ArgumentList.Add("-Doolloo.jlw.tmpdir=" + options.NativesDirectory);
            start.ArgumentList.Add("-jar");
            start.ArgumentList.Add(Path.GetFullPath(wrapper));
        }

        start.ArgumentList.Add(root.GetProperty("mainClass").GetString()!);
        foreach (string argument in game) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>提供启动用的原生标记; 可用于启动前命令与标题, 调用方不得记录含令牌的展开结果.</summary>
    public static MinecraftLaunchPlaceholderContext CreateContext(MinecraftLaunchVersion version, MinecraftLaunchFilePlan files, MinecraftLaunchOptions options)
    {
        using var document = JsonDocument.Parse(version.Json);
        string separator = options.Environment.OsName == "windows" ? ";" : ":";
        Dictionary<string, string?> native = new(options.Context.NativeValues, StringComparer.Ordinal)
        {
            ["auth_access_token"] = options.AccessToken, ["access_token"] = options.AccessToken,
            ["auth_session"] = "token:" + options.AccessToken + ":" + options.Context.PlayerUuid?.ToString("N"),
            ["user_type"] = options.IsMicrosoftAccount ? "msa" : "legacy", ["user_properties"] = "{}",
            ["auth_xuid"] = "", ["clientid"] = options.ClientId,
            ["version_type"] = document.RootElement.TryGetProperty("type", out var type) ? type.GetString() : "release",
            ["natives_directory"] = Path.GetFullPath(options.NativesDirectory),
            ["classpath"] = string.Join(separator, files.ClassPath), ["classpath_separator"] = separator,
            ["primary_jar"] = version.ClientJarPath, ["assets_index_name"] = files.AssetIndexId,
            ["game_assets"] = Path.Combine(version.GameDirectory, "assets", "virtual", files.AssetIndexId),
            ["resolution_width"] = (options.WindowWidth ?? 854).ToString(CultureInfo.InvariantCulture),
            ["resolution_height"] = (options.WindowHeight ?? 480).ToString(CultureInfo.InvariantCulture),
            ["launcher_name"] = options.Context.LauncherName, ["launcher_version"] = options.Context.LauncherVersion
        };
        return options.Context with
        {
            GameDirectory = version.GameDirectory, GameInstanceDirectory = options.WorkingDirectory,
            JavaDirectory = Path.GetDirectoryName(options.Java.Path), VersionDirectory = version.VersionDirectory,
            NativeValues = native
        };
    }

    /// <summary>按双引号和反斜杠规则拆分用户参数; 不会执行 shell 展开.</summary>
    public static IReadOnlyList<string> SplitArguments(string text)
    {
        List<string> result = [];
        StringBuilder current = new();
        bool quoted = false, started = false;
        for (int index = 0; index < text.Length; index++)
        {
            char c = text[index];
            if (c == '\\')
            {
                int start = index;
                while (index < text.Length && text[index] == '\\') index++;
                int count = index - start;
                if (index < text.Length && text[index] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 != 0) current.Append('"');
                    else quoted = !quoted;
                }
                else
                {
                    current.Append('\\', count);
                    index--;
                }

                started = true;
            }
            else if (c == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }

        if (quoted) throw new FormatException("启动参数中的双引号没有配对.");
        if (started) result.Add(current.ToString());
        return result;
    }

    private static void ApplyGarbageCollector(List<string> args, MinecraftLaunchOptions options)
    {
        if (options.GarbageCollector == MinecraftGarbageCollector.Custom) return;
        args.RemoveAll(value => GcArgument().IsMatch(value));
        bool zgc = options.Java.MajorVersion >= (options.GarbageCollector == MinecraftGarbageCollector.PreferGenerationalZgc ? 21 : 15)
                   && options.GarbageCollector is MinecraftGarbageCollector.PreferZgc or MinecraftGarbageCollector.PreferGenerationalZgc
                   && (options.Environment.OsName != "windows" || Version.TryParse(options.Environment.OsVersion, out var osVersion) && osVersion.Build >= 17763);
        args.Add("-XX:+UnlockExperimentalVMOptions");
        args.Add(zgc ? "-XX:+UseZGC" : "-XX:+UseG1GC");
        if (zgc && options.Java.MajorVersion is 21 or 22) args.Add("-XX:+ZGenerational");
        if (!zgc)
        {
            args.AddRange(["-XX:G1NewSizePercent=20", "-XX:G1ReservePercent=20", "-XX:G1HeapRegionSize=32M", "-XX:MaxGCPauseMillis=50"]);
            if (options.GarbageCollector == MinecraftGarbageCollector.TunedG1)
            {
                args.Add("-XX:+PerfDisableSharedMem");
                if (options.Java.MajorVersion == 8) args.Add("-XX:+ParallelRefProcEnabled");
                if (options.Java.MajorVersion >= 12) args.AddRange(["-XX:MinHeapFreeRatio=25", "-XX:MaxHeapFreeRatio=40"]);
            }
        }
    }

    private static List<string> DeduplicateJvm(List<string> args)
    {
        Dictionary<string, int> positions = new(StringComparer.Ordinal);
        List<string> result = [];
        for (int index = 0; index < args.Count; index++)
        {
            string value = args[index];
            bool pair = value is "-cp" or "-classpath" or "--class-path";
            string? key = pair ? "classpath" : value.StartsWith("-D", StringComparison.Ordinal) ? value.Split('=')[0] : null;
            if (key is not null && positions.TryGetValue(key, out int previous))
            {
                if (pair) result[previous + 1] = args[++index];
                else result[previous] = value;
                continue;
            }

            if (key is not null) positions[key] = result.Count;
            result.Add(value);
            if (pair)
            {
                if (++index >= args.Count) throw new FormatException("类路径参数缺少值.");
                result.Add(args[index]);
            }
        }

        return result;
    }

    private static void SetGameArgument(List<string> args, string name, string? value)
    {
        for (int index = args.Count - 1; index >= 0; index--)
            if (args[index] == name)
            {
                if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) args.RemoveAt(index + 1);
                args.RemoveAt(index);
            }

        args.Add(name);
        if (value is not null) args.Add(value);
    }

    private static List<string> DeduplicateGame(List<string> args)
    {
        List<string> result = [];
        for (int index = 0; index < args.Count; index++)
        {
            string key = args[index];
            if (key.StartsWith("--", StringComparison.Ordinal) && key != "--tweakClass" && index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                SetGameArgument(result, key, args[++index]);
            else result.Add(key);
        }

        return result;
    }

    [GeneratedRegex(@"^-XX:[+-]?(Use\w+GC|ZGenerational|G1\w+(Percent|Size)|MaxGCPauseMillis|MinHeapFreeRatio|MaxHeapFreeRatio)(=|$)")]
    private static partial Regex GcArgument();
}
