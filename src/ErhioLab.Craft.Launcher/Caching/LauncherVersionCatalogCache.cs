using System.IO;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Caching;

internal static class LauncherVersionCatalogCache
{
    private static readonly TimeSpan Freshness = TimeSpan.FromHours(12);

    internal static MinecraftVersionCatalog? Load(LauncherCache cache)
    {
        string path = cache.Catalog;
        if (!File.Exists(path) || CacheFileTree.IsLinked(path)) return null;
        var catalog = JsonSerializer.Deserialize<MinecraftVersionCatalog>(File.ReadAllBytes(path));
        if (
            catalog is not { Versions.Count: > 0 } ||
            string.IsNullOrWhiteSpace(catalog.LatestReleaseId) || string.IsNullOrWhiteSpace(catalog.LatestPreviewId) ||
            !catalog.Versions.Any(version => version.Id == catalog.LatestReleaseId) ||
            !catalog.Versions.Any(version => version.Id == catalog.LatestPreviewId)
        )
            throw new JsonException("游戏版本列表缓存不完整.");
        return catalog;
    }

    internal static async Task SaveAsync(LauncherCache cache, MinecraftVersionCatalog catalog, Action<Exception> onError, CancellationToken token)
    {
        try
        {
            await LauncherCache.WriteAsync(cache.Catalog, JsonSerializer.SerializeToUtf8Bytes(catalog), token).ConfigureAwait(false);
        }
        catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
        {
            onError(error);
        }
    }

    internal static async Task<MinecraftVersion?> FindAsync(LauncherCache cache, MinecraftVersionCatalogClient client, string id,
        MinecraftManifestSource preferred, Action<Exception> onError, CancellationToken token)
    {
        MinecraftVersion? known = null;
        try
        {
            var cached = await Task.Run(() => Load(cache), token).ConfigureAwait(false);
            known = cached?.Versions.FirstOrDefault(version => version.Id == id);
            if (known is not null && File.GetLastWriteTimeUtc(cache.Catalog) >= DateTime.UtcNow - Freshness) return known;
        }
        catch (Exception error) when (CacheFileTree.IsFileSystemError(error) || error is JsonException or NotSupportedException)
        {
            onError(error);
        }

        try
        {
            var refreshed = await client.GetCatalogAsync(preferred, token).ConfigureAwait(false);
            await SaveAsync(cache, refreshed, onError, token).ConfigureAwait(false);
            return refreshed.Versions.FirstOrDefault(version => version.Id == id);
        }
        catch (MinecraftVersionCatalogException error) when (known is not null)
        {
            onError(error);
            return known;
        }
    }
}
