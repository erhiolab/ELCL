using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ErhioLab.Craft.Launcher.Core.Accounts;

namespace ErhioLab.Craft.Launcher.Accounts;

internal sealed record SkinAvatar(BitmapSource Face, BitmapSource Hat);

/// <summary>缓存 Microsoft 账户的皮肤 PNG，并从脸部及帽子层生成共用头像.</summary>
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

    internal void RefreshAll()
    {
        foreach (MicrosoftAccount account in App.Accounts.MicrosoftAccounts) RequestRefresh(account.Uuid);
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

            string accessToken = account.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5) ? App.Accounts.GetMinecraftAccessToken(uuid) : await RefreshTokenAsync(uuid);
            MinecraftPlayerSkin? skin;
            try
            {
                skin = await SkinClient.GetActiveSkinAsync(accessToken);
            }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
            {
                accessToken = await RefreshTokenAsync(uuid);
                skin = await SkinClient.GetActiveSkinAsync(accessToken);
            }

            if (App.Accounts.MicrosoftAccounts.All(item => item.Uuid != uuid)) return;
            if (skin is null)
            {
                RemoveCache(uuid);
                return;
            }

            byte[] bytes = await SkinClient.DownloadSkinAsync(skin);
            SkinAvatar avatar = CreateAvatar(bytes);
            Directory.CreateDirectory(cacheDirectory);
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
            App.Logger.Information("Skins", $"已缓存 Microsoft 账户皮肤: {uuid:N}.");
        }
        catch (Exception error)
        {
            App.Logger.Warning("Skins", $"无法更新 Microsoft 账户皮肤: {uuid:N}, {error.Message}");
        }
        finally
        {
            lock (_refreshing) _refreshing.Remove(uuid);
        }
    }

    private static async Task<string> RefreshTokenAsync(Guid uuid)
    {
        string refreshToken = App.Accounts.GetMicrosoftRefreshToken(uuid);
        MicrosoftMinecraftSession session = await AuthClient.RefreshAsync(App.Services.MicrosoftClientId, refreshToken);
        if (session.Uuid != uuid) throw new InvalidDataException("刷新令牌返回了不同的 Minecraft 账户.");
        if (App.Accounts.MicrosoftAccounts.Any(item => item.Uuid == uuid)) App.Accounts.AddOrUpdateMicrosoft(session, selectAccount: false);
        return session.MinecraftAccessToken;
    }

    private string CachePath(Guid uuid) => Path.Combine(cacheDirectory, uuid.ToString("N") + ".png");

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
        return new SkinAvatar(face, hat);
    }
}
