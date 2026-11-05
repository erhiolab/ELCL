using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;

namespace ErhioLab.Craft.Launcher.Localization;

/// <summary>从构建内的 JSON 文件加载界面语言，并更新 WPF 动态资源.</summary>
public static class LocalizationManager
{
    public const string DefaultLanguage = "zh_cn";
    private const string Prefix = "ErhioLab.Craft.Launcher.Languages.";
    private const string ResourcePrefix = "I18n.";
    private static readonly Assembly Assembly = typeof(LocalizationManager).Assembly;

    private static readonly IReadOnlyDictionary<string, string> ResourceNames = Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(name => name[Prefix.Length..^5], StringComparer.OrdinalIgnoreCase);

    private static ResourceDictionary? _currentResources;
    private static IReadOnlyDictionary<string, string> _currentStrings = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string>? _fallbackStrings;

    public static IReadOnlyList<string> AvailableLanguages => ResourceNames.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string CurrentLanguage { get; private set; } = DefaultLanguage;
    public static event EventHandler? Changed;
    public static bool IsAvailable(string? language) => language is not null && ResourceNames.ContainsKey(language);

    public static string GetName(string language) => ReadPack(language).Name;

    internal static string GetForLanguage(string language, string key)
    {
        LanguagePack pack = ReadPack(language);
        return pack.Strings.GetValueOrDefault(key) ?? (_fallbackStrings ??= ReadPack(DefaultLanguage).Strings).GetValueOrDefault(key, key);
    }

    public static string Get(string key) => _currentStrings.TryGetValue(key, out string? value)
        ? value
        : (_fallbackStrings ??= ReadPack(DefaultLanguage).Strings).GetValueOrDefault(key, key);

    public static void Apply(string language)
    {
        if (!IsAvailable(language)) language = DefaultLanguage;
        LanguagePack pack = ReadPack(language);
        _fallbackStrings ??= ReadPack(DefaultLanguage).Strings;
        ResourceDictionary resources = new();
        foreach (string key in _fallbackStrings.Keys.Concat(pack.Strings.Keys).Distinct(StringComparer.Ordinal))
            resources[ResourcePrefix + key] = pack.Strings.GetValueOrDefault(key) ?? _fallbackStrings.GetValueOrDefault(key) ?? key;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_currentResources is not null) dictionaries.Remove(_currentResources);
        dictionaries.Add(resources);
        _currentResources = resources;
        _currentStrings = pack.Strings;
        CurrentLanguage = language;
        CultureInfo culture = CultureInfo.GetCultureInfo(language.Replace('_', '-'));
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        if (Application.Current.MainWindow is { } window)
            window.Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static LanguagePack ReadPack(string language)
    {
        if (!ResourceNames.TryGetValue(language, out string? resourceName))
            throw new InvalidOperationException($"找不到语言文件: {language}.");
        using Stream stream = Assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"无法读取语言文件: {language}.");
        LanguagePack? pack = JsonSerializer.Deserialize<LanguagePack>(stream);
        if (pack is null || string.IsNullOrWhiteSpace(pack.Name) || pack.Strings is null)
            throw new JsonException($"语言文件无效: {language}.");
        return pack;
    }

    private sealed record LanguagePack(string Name, Dictionary<string, string> Strings);
}
