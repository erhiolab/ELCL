using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Tasks;

namespace ErhioLab.Craft.Launcher.Versions;

internal sealed record VersionLoaderEditContext(
    string GameDirectory,
    LocalMinecraftVersion Installed,
    MinecraftVersion BaseVersion,
    IReadOnlyList<MinecraftLoaderSelection> Loaders,
    VersionLoaderEdit Operation);

internal static partial class VersionManagementService
{
    private static readonly HttpClient VersionEditHttp = new();

    internal static async Task<VersionLoaderEditContext> PrepareLoaderEditAsync(string gameDirectory, LocalMinecraftVersion version,
        CancellationToken token)
    {
        RequireIdle(version);
        var preferred = App.Settings.VersionListSource == VersionListSource.MirrorFirst ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
        var local = await Task.Run(async () =>
        {
            VersionLoaderEdit.ValidateTarget(gameDirectory, version);
            RequireUnreferenced(gameDirectory, version);
            string json = await File.ReadAllTextAsync(version.JsonPath, token);
            var metadata = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException(L("versionSettings.invalidVersion"));
            string gameId = await ReadGameIdAsync(gameDirectory, version, metadata, token);
            return (GameId: gameId, Loaders: VersionLoaderMetadata.ReadSelections(metadata, version),
                Edit: new VersionLoaderEdit(gameDirectory, version, json, VersionLoaderMetadata.ReadManagedMod(metadata)));
        }, token);

        var catalog = new MinecraftVersionCatalogClient(VersionEditHttp);
        var baseVersion = await LauncherVersionCatalogCache.FindAsync(LauncherCache.Shared, catalog, local.GameId, preferred,
            error => App.Logger.Warning("Versions", $"获取修改版本用的原版清单时出现问题: {error.Message}"), token);
        if (baseVersion is null) throw new InvalidOperationException(L("versionEdit.unknownGameVersion"));
        return new(gameDirectory, version, baseVersion, local.Loaders, local.Edit);
    }

    internal static LauncherTask ModifyLoaders(VersionLoaderEditContext edit, IReadOnlyList<MinecraftLoaderSelection> loaders)
    {
        LocalMinecraftVersion version = edit.Installed;
        RequireIdle(version);
        VersionLoaderEdit.ValidateTarget(edit.GameDirectory, version);
        var options = LauncherDownloadPolicy.Capture();
        var source = App.Settings.FileDownloadSource == VersionListSource.MirrorFirst ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
        var operation = App.Tasks.CreateMinecraftInstall(edit.BaseVersion, version.Id, edit.GameDirectory,
            source, options, loaders, edit: edit.Operation);
        var primary = loaders.FirstOrDefault();
        var updated = version with
        {
            GameVersion = edit.BaseVersion.Id,
            Loader = primary?.Kind.ToString(),
            LoaderVersion = primary?.Version.Version
        };
        string Title() => string.Format(L("versionEdit.task"), version.Id);
        var task = App.Tasks.Start(Title(), operation.Stages, version.FolderPath,
            onCompleted: () => Changed?.Invoke(version.FolderPath, updated), cleanupOnFailure: operation.Cleanup);
        task.LocalizedTitle = Title;
        App.Logger.Information("Versions", $"已创建加载器修改任务: {version.FolderPath}, 加载器={string.Join(",", loaders.Select(item => item.Kind + ":" + item.Version.Version))}.");
        return task;
    }

    private static async Task<string> ReadGameIdAsync(string gameDirectory, LocalMinecraftVersion version, JsonObject metadata,
        CancellationToken token)
    {
        static string? Text(JsonNode? node) => VersionLoaderMetadata.ReadString(node);
        if (Text(metadata["clientVersion"]) is { Length: > 0 } client) return client;
        if (metadata["arguments"] is JsonObject arguments && arguments["game"] is JsonArray game)
            for (int i = 0; i + 1 < game.Count; i++)
                if (Text(game[i]) == "--fml.mcVersion" && Text(game[i + 1]) is { Length: > 0 } fml)
                    return fml;
        if (Text(metadata["inheritsFrom"]) is { Length: > 0 } parent)
        {
            try
            {
                var launch = await MinecraftLaunchVersion.LoadAsync(gameDirectory, version.JsonPath, cancellationToken: token);
                return launch.GameVersion;
            }
            catch (FileNotFoundException)
            {
                return parent;
            }
            catch (DirectoryNotFoundException)
            {
                return parent;
            }
        }

        // 已重命名且没有 clientVersion 的旧安装, 优先从客户端自身读取版本, 不解析目录名.
        string jar = Path.Combine(version.FolderPath, Path.GetFileName(version.FolderPath) + ".jar");
        if (File.Exists(jar) && !CacheFileTree.IsLinked(jar))
        {
            try
            {
                using var zip = ZipFile.OpenRead(jar);
                if (zip.GetEntry("version.json") is { Length: < 65536 } entry)
                {
                    await using var stream = entry.Open();
                    var descriptor = await JsonNode.ParseAsync(stream, cancellationToken: token);
                    if (descriptor is JsonObject obj && Text(obj["id"]) is { Length: > 0 } id) return id;
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
            {
                App.Logger.Warning("Versions", $"无法读取客户端 JAR 内的版本信息: {error.Message}");
            }
        }

        return Text(metadata["id"]) ?? throw new InvalidDataException(L("versionEdit.unknownGameVersion"));
    }
}
