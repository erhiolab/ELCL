using System.Reflection;

namespace ErhioLab.Craft.Launcher;

internal static class LauncherHttpUserAgent
{
    internal static string Value { get; } = CreateValue();

    private static string CreateValue()
    {
        var assembly = typeof(App).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var metadataStart = version.IndexOf('+');
        return "ELCL/" + (metadataStart < 0 ? version : version[..metadataStart]);
    }
}
