using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Tasks;

namespace ErhioLab.Craft.Launcher.Accounts;

internal sealed record SkinAvatar(BitmapSource Face, BitmapSource Hat, BitmapSource Skin);

/// <summary>缓存 Microsoft 账户的皮肤 PNG, 并从脸部及帽子层生成共用头像.</summary>
internal sealed class LauncherSkinService(LauncherCache cache)
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly MinecraftSkinClient SkinClient = new(HttpClient);
    private static readonly HttpClient TextureHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Dictionary<Guid, SkinAvatar> _avatars = [];
    private readonly HashSet<Guid> _refreshing = [];
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _capeRequests = new(StringComparer.OrdinalIgnoreCase);

    internal event Action<Guid>? AvatarChanged;

    internal SkinAvatar? GetAvatar(Guid uuid)
    {
        if (_avatars.TryGetValue(uuid, out SkinAvatar? avatar)) return avatar;
        string path = CachePath(uuid);
        if (!File.Exists(path)) return null;
        try
        {
            avatar = CreateAvatar(File.ReadAllBytes(path));
            LauncherCache.Touch(path);
            _avatars[uuid] = avatar;
            return avatar;
        }
        catch (Exception error)
        {
            App.Logger.Warning("Skins", $"缓存的皮肤无法读取: {uuid:N}, {error.Message}");
            return null;
        }
    }

    internal byte[]? GetCachedSkinBytes(Guid uuid)
    {
        string path = CachePath(uuid);
        if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path);
        LauncherCache.Touch(path);
        return bytes;
    }

    internal void RefreshAll()
    {
        foreach (MicrosoftAccount account in App.Accounts.MicrosoftAccounts) RequestRefresh(account.Uuid);
    }

    internal Task<MinecraftPlayerAppearance> GetAppearanceAsync(Guid uuid, CancellationToken cancellationToken = default) => ExecuteAuthenticatedAsync(uuid, token => SkinClient.GetAppearanceAsync(token, cancellationToken));

    internal Task<byte[]> DownloadCapeAsync(Guid uuid, MinecraftPlayerCape cape, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = CapeCachePath(cape);
        var request = _capeRequests.GetOrAdd(path, _ => new Lazy<Task<byte[]>>(() => LoadCapeAsync()));
        return request.Value.WaitAsync(cancellationToken);

        async Task<byte[]> LoadCapeAsync()
        {
            try
            {
                var shared = await ReadCapeAsync(path, cape);
                if (shared is not null) return shared;
                string legacy = Path.Combine(cache.Capes, uuid.ToString("N"), Path.GetFileName(path));
                byte[] png = await ReadCapeAsync(legacy, cape) ?? await ReadCapeAsync(path, cape)
                    ?? await new MinecraftSkinClient(TextureHttp, LauncherDownloadPolicy.Capture()).DownloadCapeAsync(cape);
                if (!IsValidCape(png)) throw new InvalidDataException("披风图片尺寸无效.");
                try { await LauncherCache.WriteAsync(path, png); }
                catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
                {
                    App.Logger.Error("Skins", $"无法保存披风缓存: {cape.Id}.", error);
                }
                return png;
            }
            finally { _capeRequests.TryRemove(path, out _); }
        }
    }

    private static async Task<byte[]?> ReadCapeAsync(string path, MinecraftPlayerCape cape)
    {
        try
        {
            if (!File.Exists(path) || CacheFileTree.IsLinked(path) || new FileInfo(path).Length > 2 * 1024 * 1024) return null;
            byte[] bytes = await File.ReadAllBytesAsync(path);
            if (!IsValidCape(bytes)) return null;
            LauncherCache.Touch(path);
            return bytes;
        }
        catch (Exception error) when (CacheFileTree.IsFileSystemError(error))
        {
            App.Logger.Warning("Skins", $"无法读取披风缓存: {cape.Id}, {error.Message}");
            return null;
        }
    }

    internal void RemoveCapeCache(Guid uuid, MinecraftPlayerCape cape)
    {
        try
        {
            string path = CapeCachePath(cape);
            if (!CacheFileTree.IsLinked(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Warning("Skins", $"无法删除无效披风缓存: account={uuid:N}, cape={cape.Id}, {error.Message}");
        }
    }

    internal async Task UploadSkinAsync(Guid uuid, byte[] png, string variant, CancellationToken cancellationToken = default)
    {
        await ExecuteAuthenticatedAsync(uuid, async token =>
        {
            await SkinClient.UploadSkinAsync(token, png, variant, cancellationToken);
            return true;
        });
        if (App.Accounts.MicrosoftAccounts.Any(account => account.Uuid == uuid)) await StoreSkinAsync(uuid, png);
    }

    internal async Task SetActiveCapeAsync(Guid uuid, string? capeId, CancellationToken cancellationToken = default)
    {
        await ExecuteAuthenticatedAsync(uuid, async token =>
        {
            await SkinClient.SetActiveCapeAsync(token, capeId, cancellationToken);
            return true;
        });
    }

    internal void RequestRefresh(Guid uuid, bool force = false)
    {
        lock (_refreshing)
        {
            if (!_refreshing.Add(uuid)) return;
        }

        _ = RefreshAsync(uuid, force);
    }

    internal void RemoveCache(Guid uuid) => RemoveSkinCache(uuid);

    private void RemoveSkinCache(Guid uuid)
    {
        _avatars.Remove(uuid);
        try
        {
            string path = CachePath(uuid);
            if (!CacheFileTree.IsLinked(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Warning("Skins", $"无法删除皮肤缓存: {uuid:N}, {error.Message}");
        }

        AvatarChanged?.Invoke(uuid);
    }

    private async Task RefreshAsync(Guid uuid, bool force)
    {
        try
        {
            MicrosoftAccount? account = App.Accounts.MicrosoftAccounts.FirstOrDefault(item => item.Uuid == uuid);
            if (account is null) return;
            string path = CachePath(uuid);
            if (!force && File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddMinutes(-30) && GetAvatar(uuid) is not null) return;

            MinecraftPlayerSkin? skin = await ExecuteAuthenticatedAsync(uuid, token => SkinClient.GetActiveSkinAsync(token));

            if (App.Accounts.MicrosoftAccounts.All(item => item.Uuid != uuid)) return;
            if (skin is null)
            {
                RemoveSkinCache(uuid);
                return;
            }

            byte[] bytes = await new MinecraftSkinClient(TextureHttp, LauncherDownloadPolicy.Capture()).DownloadSkinAsync(skin);
            await StoreSkinAsync(uuid, bytes);
            App.Logger.Information("Skins", $"已缓存 Microsoft 账户皮肤: {uuid:N}.");
        }
        catch (Exception error)
        {
            App.Logger.Error("Skins", $"无法更新 Microsoft 账户皮肤: {uuid:N}.", error);
        }
        finally
        {
            lock (_refreshing) _refreshing.Remove(uuid);
        }
    }

    private async Task StoreSkinAsync(Guid uuid, byte[] bytes)
    {
        SkinAvatar avatar = CreateAvatar(bytes);
        await LauncherCache.WriteAsync(CachePath(uuid), bytes);

        _avatars[uuid] = avatar;
        AvatarChanged?.Invoke(uuid);
    }

    private static async Task<string> RefreshTokenAsync(Guid uuid)
    {
        MicrosoftMinecraftSession session = await LauncherMicrosoftSessions.GetAsync(uuid, HttpClient, forceRefresh: true);
        return session.MinecraftAccessToken;
    }

    private static async Task<T> ExecuteAuthenticatedAsync<T>(Guid uuid, Func<string, Task<T>> action)
    {
        MicrosoftAccount account = App.Accounts.MicrosoftAccounts.FirstOrDefault(item => item.Uuid == uuid)
            ?? throw new ArgumentException("Microsoft 账户不存在.", nameof(uuid));
        string token = account.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5)
            ? App.Accounts.GetMinecraftAccessToken(uuid) : await RefreshTokenAsync(uuid);
        try
        {
            return await action(token);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await action(await RefreshTokenAsync(uuid));
        }
    }

    private string CachePath(Guid uuid) => Path.Combine(cache.Skins, uuid.ToString("N") + ".png");

    private string CapeCachePath(MinecraftPlayerCape cape)
    {
        string key = LauncherCache.Key(cape.Id + "\n" + cape.Url.AbsoluteUri);
        return Path.Combine(cache.Capes, key + ".png");
    }

    private static bool IsValidCape(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length is < 24 or > 2 * 1024 * 1024 || !png.StartsWith(signature) ||
            !png.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        int width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        return width is >= 64 and <= 1024 && width % 64 == 0 && height == width / 2;
    }

    private static SkinAvatar CreateAvatar(byte[] png)
    {
        using MemoryStream stream = new(png, writable: false);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapFrame skin = decoder.Frames[0];
        if (skin.PixelWidth < 64 || skin.PixelWidth > 1024 || skin.PixelWidth % 64 != 0 ||
            skin.PixelHeight != skin.PixelWidth && skin.PixelHeight != skin.PixelWidth / 2)
            throw new InvalidDataException("皮肤图片尺寸无效.");

        int scale = skin.PixelWidth / 64;
        CroppedBitmap face = new(skin, new Int32Rect(8 * scale, 8 * scale, 8 * scale, 8 * scale));
        CroppedBitmap hat = new(skin, new Int32Rect(40 * scale, 8 * scale, 8 * scale, 8 * scale));
        face.Freeze();
        hat.Freeze();
        skin.Freeze();
        return new SkinAvatar(face, hat, skin);
    }
}
