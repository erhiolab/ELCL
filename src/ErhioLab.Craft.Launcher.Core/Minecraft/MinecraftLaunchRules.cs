using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>版本规则使用的目标环境, 不依赖启动器设置.</summary>
public sealed record MinecraftLaunchEnvironment(string OsName, string Architecture, string OsVersion)
{
    public static MinecraftLaunchEnvironment Current => new(
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux",
        RuntimeInformation.OSArchitecture switch { System.Runtime.InteropServices.Architecture.X86 => "x86", System.Runtime.InteropServices.Architecture.Arm64 => "aarch64", _ => "amd64" },
        Environment.OSVersion.Version.ToString());

    internal string NativeBits => Architecture is "x86" or "i386" ? "32" : "64";
}

internal static class MinecraftLaunchRules
{
    internal static bool Allows(JsonElement item, MinecraftLaunchEnvironment environment, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (!item.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array) return true;
        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            bool matches = true;
            if (rule.TryGetProperty("os", out var os))
            {
                matches &= Match(os, "name", environment.OsName);
                matches &= Match(os, "arch", environment.Architecture);
                if (os.TryGetProperty("version", out var version))
                    matches &= Regex.IsMatch(environment.OsVersion, version.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }

            if (rule.TryGetProperty("features", out var required))
                foreach (var feature in required.EnumerateObject())
                    matches &= (features?.GetValueOrDefault(feature.Name) ?? false) == feature.Value.GetBoolean();
            if (matches) allowed = rule.GetProperty("action").GetString() == "allow";
        }

        return allowed;
    }

    private static bool Match(JsonElement os, string key, string value) => !os.TryGetProperty(key, out var actual) || actual.GetString() == value;
}
