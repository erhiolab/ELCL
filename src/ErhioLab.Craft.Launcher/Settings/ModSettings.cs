namespace ErhioLab.Craft.Launcher.Settings;

public enum ModFileNameFormat
{
    Original,
    ChineseBrackets,
    SquareBrackets,
    NamePrefix,
    NameSuffix
}

public enum ModListPresentation
{
    NameFirst,
    FileNameFirst
}

internal static class ModLocalization
{
    internal static bool SupportsLanguage(string? language) => language is not null
        && (language.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh_", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-", StringComparison.OrdinalIgnoreCase));

    internal static bool IsEnabled(LauncherSettings settings) => SupportsLanguage(settings.Language) && settings.ModTranslationEnabled;

    internal static ModFileNameFormat FileNameFormat(LauncherSettings settings) => IsEnabled(settings)
        ? settings.ModFileNameFormat : ModFileNameFormat.Original;
}
