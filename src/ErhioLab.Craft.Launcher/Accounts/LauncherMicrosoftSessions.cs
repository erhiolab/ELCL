using System.IO;
using System.Net.Http;

using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Accounts;

/// <summary>串行刷新 Microsoft 令牌, 避免启动和皮肤加载同时刷新后覆盖新凭据.</summary>
internal static class LauncherMicrosoftSessions
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<MicrosoftMinecraftSession> GetAsync(Guid uuid, HttpClient http, bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            MicrosoftAccount account = App.Accounts.MicrosoftAccounts.FirstOrDefault(value => value.Uuid == uuid)
                ?? throw new InvalidOperationException("Microsoft 账户已被移除.");
            var auth = new MicrosoftMinecraftAuthClient(http);
            string refresh = App.Accounts.GetMicrosoftRefreshToken(uuid);
            MicrosoftMinecraftSession session;
            if (!forceRefresh && account.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                string token = App.Accounts.GetMinecraftAccessToken(uuid);
                try
                {
                    var profile = await auth.ValidateMinecraftSessionAsync(token, cancellationToken);
                    session = new(profile.UserName, profile.Uuid, token, account.AccessTokenExpiresAt, refresh);
                }
                catch (MicrosoftMinecraftAuthException error) when (error.Code.Equals("UNAUTHORIZED", StringComparison.OrdinalIgnoreCase))
                {
                    session = await auth.RefreshAsync(ServiceConfiguration.MicrosoftClientId, refresh, cancellationToken);
                }
            }
            else session = await auth.RefreshAsync(ServiceConfiguration.MicrosoftClientId, refresh, cancellationToken);
            if (session.Uuid != uuid) throw new InvalidDataException("登录服务返回了不同的 Minecraft 账户.");
            cancellationToken.ThrowIfCancellationRequested();
            if (App.Accounts.MicrosoftAccounts.All(value => value.Uuid != uuid)) throw new InvalidOperationException("登录期间账户已被移除.");
            App.Accounts.AddOrUpdateMicrosoft(session, selectAccount: false);
            return session;
        }
        finally { Gate.Release(); }
    }
}
