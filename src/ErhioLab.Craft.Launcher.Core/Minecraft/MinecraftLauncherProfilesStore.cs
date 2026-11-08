using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>官方启动器安装档案的数据. 账户和访问令牌不属于此模型.</summary>
public sealed record MinecraftLauncherProfile(string Name, string VersionId, string? GameDirectory = null, string Icon = "Grass");

/// <summary>保留未知字段地更新 launcher_profiles JSON; 路径/档案 ID 和隔离目录由调用方决定.</summary>
public sealed class MinecraftLauncherProfilesStore
{
    public const string LegacyFileName = "launcher_profiles.json";
    public const string MicrosoftStoreFileName = "launcher_profiles_microsoft_store.json";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate;
    public string FilePath { get; }

    public MinecraftLauncherProfilesStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        _gate = Gates.GetOrAdd(FilePath, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>仅在文件不存在时创建 profiles/settings 空对象; 现有文件即使无效也不会被覆盖.</summary>
    public async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(FilePath)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = TemporaryPath();
            try
            {
                await File.WriteAllTextAsync(temporary, EmptyDocument().ToJsonString(JsonOptions), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporary, FilePath, overwrite: false);
                }
                catch (IOException) when (File.Exists(FilePath))
                {
                    return false;
                }

                return true;
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>新增或更新指定档案, 保留其他档案和未知字段. markUsed 只更新该档案的最后使用时间.</summary>
    public Task UpsertProfileAsync(string profileId, MinecraftLauncherProfile profile, bool markUsed = false,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profileId, profile);
        return UpdateAsync(root => SetProfile(Profiles(root), profileId, profile, markUsed), cancellationToken);
    }

    /// <summary>在一次写入中登记多个档案; 不删除未列出的档案, 内容不变时不重写文件.</summary>
    public Task UpsertProfilesAsync(IReadOnlyDictionary<string, MinecraftLauncherProfile> profiles, bool markUsed = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = profiles.ToArray();
        foreach (var entry in entries) ValidateProfile(entry.Key, entry.Value);
        if (entries.Length == 0) return Task.CompletedTask;
        return UpdateAsync(root =>
        {
            var target = Profiles(root);
            // 使用非短路或，保证前面的条目有变化时仍会处理其余条目。
            return entries.Aggregate(false, (changed, entry) => changed | SetProfile(target, entry.Key, entry.Value, markUsed));
        }, cancellationToken);
    }

    private static void ValidateProfile(string profileId, MinecraftLauncherProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.VersionId);
    }

    private static bool SetProfile(JsonObject profiles, string profileId, MinecraftLauncherProfile profile, bool markUsed)
    {
        if (profiles.ContainsKey(profileId) && profiles[profileId] is not JsonObject)
            throw new JsonException("指定的启动器档案不是 JSON 对象.");
        var entry = profiles[profileId] as JsonObject ?? new JsonObject();
        var original = entry.DeepClone();
        entry["name"] = profile.Name;
        entry["lastVersionId"] = profile.VersionId;
        entry["type"] = "custom";
        entry["icon"] = profile.Icon;
        if (profile.GameDirectory is not null) entry["gameDir"] = Path.GetFullPath(profile.GameDirectory);
        else entry.Remove("gameDir");
        entry["created"] ??= Timestamp();
        if (markUsed) entry["lastUsed"] = Timestamp();
        else entry["lastUsed"] ??= "1970-01-01T00:00:00.000Z";
        profiles[profileId] ??= entry;
        return !JsonNode.DeepEquals(original, entry);
    }

    /// <summary>只移除指定 ID; 文件或档案不存在时不创建文件.</summary>
    public Task RemoveProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return UpdateAsync(root => Profiles(root).Remove(profileId), cancellationToken, createIfMissing: false);
    }

    private async Task UpdateAsync(Func<JsonObject, bool> update, CancellationToken cancellationToken, bool createIfMissing = true)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[]? original = null;
            if (File.Exists(FilePath)) original = await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
            else if (!createIfMissing) return;
            JsonObject root;
            if (original is null) root = EmptyDocument();
            else
            {
                ReadOnlyMemory<byte> json = original;
                if (original.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) json = json[3..];
                root = JsonNode.Parse(json.Span, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false })
                    as JsonObject ?? throw new JsonException("启动器档案文件必须是 JSON 对象.");
                _ = Profiles(root);
            }

            if (!update(root)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = TemporaryPath();
            try
            {
                await File.WriteAllTextAsync(temporary, root.ToJsonString(JsonOptions), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                if (original is not null)
                {
                    var current = await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
                    if (!original.AsSpan().SequenceEqual(current)) throw new IOException("启动器档案文件被其他程序修改, 本次更新已取消.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, FilePath, overwrite: original is not null);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static JsonObject Profiles(JsonObject root)
    {
        if (root["profiles"] is JsonObject profiles) return profiles;
        if (root.ContainsKey("profiles")) throw new JsonException("启动器档案文件的 profiles 必须是 JSON 对象.");
        JsonObject empty = new();
        root["profiles"] = empty;
        return empty;
    }

    private static JsonObject EmptyDocument() => new() { ["profiles"] = new JsonObject(), ["settings"] = new JsonObject() };
    private string TemporaryPath() => Path.Combine(Path.GetDirectoryName(FilePath)!, "." + Path.GetFileName(FilePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
    private static string Timestamp() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
