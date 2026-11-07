using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

using ErhioLab.Craft.Launcher.Core.Accounts;

namespace ErhioLab.Craft.Launcher.Accounts;

internal sealed record SkinAvatar(BitmapSource Face, BitmapSource Hat, BitmapSource Skin);

/// <summary>缓存 Microsoft 账户的皮肤 PNG, 并从脸部及帽子层生成共用头像.</summary>
internal sealed class LauncherSkinService(string cacheDirectory)
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly MinecraftSkinClient SkinClient = new(HttpClient);
    private static readonly MicrosoftMinecraftAuthClient AuthClient = new(HttpClient);
    private readonly Dictionary<Guid, SkinAvatar> _avatars = [];
    private readonly HashSet<Guid> _refreshing = [];

    internal event Action<Guid>? AvatarChanged;

    internal SkinAvatar? GetAvatar(Guid uuid)
    {
        if (_avatars.TryGetValue(uuid, out SkinAvatar? avatar)) return avatar;
        string path = CachePath(uuid);
        if (!File.Exists(path)) return null;
        try
        {
            avatar = CreateAvatar(File.ReadAllBytes(path));
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
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    internal void RefreshAll()
    {
        foreach (MicrosoftAccount account in App.Accounts.MicrosoftAccounts) RequestRefresh(account.Uuid);
    }

    internal Task<MinecraftPlayerAppearance> GetAppearanceAsync(Guid uuid, CancellationToken cancellationToken = default) => ExecuteAuthenticatedAsync(uuid, token => SkinClient.GetAppearanceAsync(token, cancellationToken));

    internal async Task<byte[]> DownloadCapeAsync(Guid uuid, MinecraftPlayerCape cape, CancellationToken cancellationToken = default)
    {
        string path = CapeCachePath(uuid, cape);
        if (File.Exists(path))
        {
            try
            {
                byte[] cached = await File.ReadAllBytesAsync(path, cancellationToken);
                if (IsValidCape(cached))
                {
                    App.Logger.Information("Skins", $"已从缓存读取披风: account={uuid:N}, cape={cape.Id}.");
                    return cached;
                }

                File.Delete(path);
                App.Logger.Warning("Skins", $"披风缓存无效，重新下载: account={uuid:N}, cape={cape.Id}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                App.Logger.Warning("Skins", $"无法读取披风缓存，重新下载: account={uuid:N}, cape={cape.Id}, {error.Message}");
            }
        }

        byte[] png = await SkinClient.DownloadCapeAsync(cape, cancellationToken);
        if (!IsValidCape(png)) throw new InvalidDataException("披风图片尺寸无效.");
        cancellationToken.ThrowIfCancellationRequested();
        if (App.Accounts.MicrosoftAccounts.All(account => account.Uuid != uuid)) return png;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, png, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
        App.Logger.Information("Skins", $"披风已缓存: account={uuid:N}, cape={cape.Id}.");
        return png;
    }

    internal void RemoveCapeCache(Guid uuid, MinecraftPlayerCape cape)
    {
        try { File.Delete(CapeCachePath(uuid, cape)); }
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

    internal void RemoveCache(Guid uuid)
    {
        RemoveSkinCache(uuid);
        try
        {
            string capeDirectory = Path.Combine(cacheDirectory, "capes", uuid.ToString("N"));
            if (Directory.Exists(capeDirectory)) Directory.Delete(capeDirectory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Warning("Skins", $"无法删除披风缓存: {uuid:N}, {error.Message}");
        }
    }

    private void RemoveSkinCache(Guid uuid)
    {
        _avatars.Remove(uuid);
        try
        {
            File.Delete(CachePath(uuid));
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

            byte[] bytes = await SkinClient.DownloadSkinAsync(skin);
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
        Directory.CreateDirectory(cacheDirectory);
        string path = CachePath(uuid);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }

        _avatars[uuid] = avatar;
        AvatarChanged?.Invoke(uuid);
    }

    private static async Task<string> RefreshTokenAsync(Guid uuid)
    {
        string refreshToken = App.Accounts.GetMicrosoftRefreshToken(uuid);
        MicrosoftMinecraftSession session = await AuthClient.RefreshAsync(App.Services.MicrosoftClientId, refreshToken);
        if (session.Uuid != uuid) throw new InvalidDataException("刷新令牌返回了不同的 Minecraft 账户.");
        if (App.Accounts.MicrosoftAccounts.Any(item => item.Uuid == uuid)) App.Accounts.AddOrUpdateMicrosoft(session, selectAccount: false);
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

    private string CachePath(Guid uuid) => Path.Combine(cacheDirectory, uuid.ToString("N") + ".png");

    private string CapeCachePath(Guid uuid, MinecraftPlayerCape cape)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cape.Id + "\n" + cape.Url.AbsoluteUri)));
        return Path.Combine(cacheDirectory, "capes", uuid.ToString("N"), key + ".png");
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
