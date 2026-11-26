using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Settings;

internal enum LauncherDataKind
{
    Settings,
    Background,
    Accounts,
    SettingsBackup,
    AccountsBackup
}

internal sealed class LauncherConfigVersionMissingException() : JsonException(LocalizationManager.Get("settings.backupMissingVersion"));

internal static class LauncherConfigVersions
{
    private const string KindProperty = "FileKind";
    private const string VersionProperty = "FormatVersion";

    // 版本 1 是起点. 每个迁移按文件类型和来源版本登记, 只升级一级.
    private static readonly FrozenDictionary<(LauncherDataKind Kind, int Version), Action<JsonObject>> Migrations =
        FrozenDictionary<(LauncherDataKind, int), Action<JsonObject>>.Empty;

    private static (string Name, int Version, bool AllowUnversioned) Describe(LauncherDataKind kind) => kind switch
    {
        LauncherDataKind.Settings => ("elcl-settings", 1, false),
        LauncherDataKind.Background => ("elcl-background", 1, true),
        LauncherDataKind.Accounts => ("elcl-accounts", 1, true),
        LauncherDataKind.SettingsBackup => ("elcl-settings-backup", 1, false),
        LauncherDataKind.AccountsBackup => ("elcl-accounts-backup", 1, false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static JsonObject Stamp(JsonObject data, LauncherDataKind kind)
    {
        var (name, version, _) = Describe(kind);
        JsonObject document = new() { [KindProperty] = name, [VersionProperty] = version };
        foreach (var field in data) document.Add(field.Key, field.Value?.DeepClone());
        return document;
    }

    internal static bool Upgrade(JsonObject document, LauncherDataKind kind)
    {
        var (name, current, allowUnversioned) = Describe(kind);
        // 原来就存在的本地字段仍按原键名读取, 不搬运已移除或改名的字段.
        bool hasKind = document.TryGetPropertyValue(KindProperty, out var declaredKind);
        bool hasVersion = document.TryGetPropertyValue(VersionProperty, out var declaredVersion);
        bool localBaseline = allowUnversioned && !hasKind && !hasVersion;
        int version = 1;
        if (!localBaseline)
        {
            if (hasKind && (declaredKind is not JsonValue kindValue || !kindValue.TryGetValue<string>(out var actual) || actual != name))
                throw new JsonException(LocalizationManager.Get("settings.backupWrongKind"));
            if (!hasVersion) throw new LauncherConfigVersionMissingException();
            if (!hasKind) throw new JsonException(LocalizationManager.Get("settings.backupWrongKind"));
            if (declaredVersion is not JsonValue versionValue || !versionValue.TryGetValue(out version) || version < 1)
                throw new JsonException(LocalizationManager.Get("settings.backupInvalidVersion"));
        }

        if (version > current)
            throw new JsonException(string.Format(LocalizationManager.Get("settings.backupNewerVersion"), version, current));

        bool changed = version != current;
        while (version < current)
        {
            if (!Migrations.TryGetValue((kind, version), out var migrate))
                throw new JsonException(string.Format(LocalizationManager.Get("settings.backupUnsupportedVersion"), version));
            migrate(document);
            document[VersionProperty] = ++version;
        }

        if (changed) document[KindProperty] = name;
        return changed;
    }
}

internal sealed class LauncherDataFile<T>(string path, LauncherDataKind kind, JsonSerializerOptions? options = null) where T : class
{
    private readonly JsonFileStore<JsonObject> _store = new(path);
    internal string FilePath => _store.FilePath;

    internal T Load(T fallback)
    {
        JsonObject document;
        try { document = ReadDocument(); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return fallback; }
        return ReadValue(document, persistUpgrade: true);
    }

    private JsonObject ReadDocument() => JsonNode.Parse(File.ReadAllText(FilePath), documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
        ?? throw new JsonException(LocalizationManager.Get("settings.backupInvalidObject"));

    internal T Read() => ReadValue(ReadDocument(), persistUpgrade: false);

    private T ReadValue(JsonObject document, bool persistUpgrade)
    {
        bool upgraded = LauncherConfigVersions.Upgrade(document, kind);
        var value = document.Deserialize<T>(options) ?? throw new JsonException(LocalizationManager.Get("settings.backupInvalidObject"));
        if (upgraded && persistUpgrade) _store.Save(document);
        return value;
    }

    internal void Save(T value)
    {
        var data = (JsonObject)JsonSerializer.SerializeToNode(value, options)!;
        _store.Save(LauncherConfigVersions.Stamp(data, kind));
    }
}
