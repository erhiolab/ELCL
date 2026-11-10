using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Versions;

/// <summary>独立于 Minecraft 元数据和全局配置的版本设置, 随版本目录一起移动.</summary>
internal sealed record VersionSettings
{
    public string Description { get; init; } = "";
    public VersionLaunchOptions? LaunchOptions { get; init; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }

    internal static string NormalizeDescription(string? value)
    {
        string text = (value ?? "").Replace("\0", "", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text[..Math.Min(text.Length, 256)];
    }
}

internal sealed class VersionSettingsStore(string versionDirectory)
{
    private readonly string _directory = Path.GetFullPath(versionDirectory);
    private readonly JsonFileStore<VersionSettings> _store = new(Path.Combine(versionDirectory, "ELCL", "settings.json"), new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    });

    internal VersionSettings Load() => _store.Load(new VersionSettings());

    internal VersionLaunchOptions SaveLaunchOptions(VersionLaunchOptions options, LauncherSettings global)
    {
        if (!Directory.Exists(_directory)) throw new DirectoryNotFoundException(_directory);
        var current = Load();
        var normalized = (options with { AdditionalData = current.LaunchOptions?.AdditionalData ?? options.AdditionalData }) .WithSettings(options.Apply(global), global);
        _store.Save(current with { LaunchOptions = normalized.HasOverrides ? normalized : null });
        return normalized;
    }

    internal void SaveDescription(string description)
    {
        if (!Directory.Exists(_directory)) throw new DirectoryNotFoundException(_directory);
        // 读取失败时不覆盖原配置; 未知字段为之后的独立启动设置保留.
        var current = Load();
        _store.Save(current with { Description = VersionSettings.NormalizeDescription(description) });
    }

    internal static string ReadDescription(string versionDirectory)
    {
        try
        {
            return VersionSettings.NormalizeDescription(new VersionSettingsStore(versionDirectory).Load().Description);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Logger.Error("Versions", $"读取版本独立配置失败, 保留原文件: {versionDirectory}.", error);
            return "";
        }
    }
}
