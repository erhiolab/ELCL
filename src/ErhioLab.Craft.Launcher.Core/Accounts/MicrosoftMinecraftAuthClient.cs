using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Accounts;

/// <summary>Microsoft 设备代码登录时展示给用户的信息.</summary>
public sealed record MicrosoftDeviceAuthorization(string DeviceCode, string UserCode, Uri VerificationUri,
    DateTimeOffset ExpiresAt, TimeSpan PollInterval);

/// <summary>调用方负责安全保存的登录结果. Core 不保存应用 ID 或令牌.</summary>
public sealed record MicrosoftMinecraftSession(string UserName, Guid Uuid, string MinecraftAccessToken,
    DateTimeOffset MinecraftAccessTokenExpiresAt, string MicrosoftRefreshToken);

/// <summary>登录服务返回的可显示错误. 不包含访问令牌.</summary>
public sealed class MicrosoftMinecraftAuthException(string stage, string code, string message) : Exception(message)
{
    public string Stage { get; } = stage;
    public string Code { get; } = code;
}

/// <summary>依次完成 Microsoft/Xbox Live/XSTS 与 Minecraft Java 身份交换.</summary>
public sealed class MicrosoftMinecraftAuthClient(HttpClient httpClient)
{
    private const string Scope = "XboxLive.SignIn XboxLive.offline_access";
    private static readonly JsonSerializerOptions XboxJsonOptions = new(JsonSerializerDefaults.General);
    private static readonly Uri DeviceCodeUrl = new("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode");
    private static readonly Uri MicrosoftTokenUrl = new("https://login.microsoftonline.com/consumers/oauth2/v2.0/token");
    private static readonly Uri XboxUserUrl = new("https://user.auth.xboxlive.com/user/authenticate");
    private static readonly Uri XstsUrl = new("https://xsts.auth.xboxlive.com/xsts/authorize");
    private static readonly Uri MinecraftLoginUrl = new("https://api.minecraftservices.com/authentication/login_with_xbox");
    private static readonly Uri EntitlementsUrl = new("https://api.minecraftservices.com/entitlements/mcstore");
    private static readonly Uri ProfileUrl = new("https://api.minecraftservices.com/minecraft/profile");

    /// <summary>请求短代码. Client ID 由调用方传入, 必须属于允许公共客户端流的应用.</summary>
    public async Task<MicrosoftDeviceAuthorization> StartDeviceAuthorizationAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        using JsonDocument result = await PostFormAsync(DeviceCodeUrl, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope
        }, "Microsoft", cancellationToken).ConfigureAwait(false);
        JsonElement root = result.RootElement;
        string verificationUri = RequiredString(root, "verification_uri", "Microsoft");
        if (!Uri.TryCreate(verificationUri, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new MicrosoftMinecraftAuthException("Microsoft", "invalid_response", "登录服务返回了无效的验证地址.");
        int expiresIn = RequiredInt(root, "expires_in", "Microsoft");
        int interval = root.TryGetProperty("interval", out JsonElement intervalValue) && intervalValue.TryGetInt32(out int seconds) ? seconds : 5;
        if (expiresIn <= 0) throw new MicrosoftMinecraftAuthException("Microsoft", "invalid_response", "登录服务返回了无效的有效期.");
        return new MicrosoftDeviceAuthorization(RequiredString(root, "device_code", "Microsoft"),
            RequiredString(root, "user_code", "Microsoft"), uri,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn), TimeSpan.FromSeconds(Math.Max(5, interval)));
    }

    /// <summary>按服务器指定的间隔轮询, 并取得 Minecraft Java 档案. 调用方取消时立即停止.</summary>
    public async Task<MicrosoftMinecraftSession> CompleteDeviceAuthorizationAsync(string clientId,
        MicrosoftDeviceAuthorization authorization, CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        ArgumentNullException.ThrowIfNull(authorization);
        TimeSpan interval = authorization.PollInterval < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : authorization.PollInterval;
        while (DateTimeOffset.UtcNow < authorization.ExpiresAt)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            if (DateTimeOffset.UtcNow >= authorization.ExpiresAt) break;
            Dictionary<string, string> form = new()
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = clientId,
                ["device_code"] = authorization.DeviceCode
            };
            using HttpResponseMessage response = await SendWithConnectionRetryAsync(() =>
            {
                HttpRequestMessage request = new(HttpMethod.Post, MicrosoftTokenUrl);
                request.Content = new FormUrlEncodedContent(form);
                return request;
            }, cancellationToken).ConfigureAwait(false);
            using JsonDocument result = await ReadJsonAsync(response, "Microsoft", allowOAuthError: true, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string error = RequiredString(result.RootElement, "error", "Microsoft");
                if (error == "authorization_pending") continue;
                if (error == "slow_down") { interval += TimeSpan.FromSeconds(5); continue; }
                throw new MicrosoftMinecraftAuthException("Microsoft", error, OAuthErrorMessage(error));
            }

            string accessToken = RequiredString(result.RootElement, "access_token", "Microsoft");
            string refreshToken = RequiredString(result.RootElement, "refresh_token", "Microsoft");
            return await ExchangeForMinecraftAsync(accessToken, refreshToken, cancellationToken).ConfigureAwait(false);
        }
        throw new MicrosoftMinecraftAuthException("Microsoft", "expired_token", "登录代码已过期, 请重新开始登录.");
    }

    /// <summary>使用调用方保存的 Microsoft 刷新令牌重新取得 Minecraft 会话.</summary>
    public async Task<MicrosoftMinecraftSession> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        using JsonDocument result = await PostFormAsync(MicrosoftTokenUrl, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        }, "Microsoft", cancellationToken).ConfigureAwait(false);
        return await ExchangeForMinecraftAsync(RequiredString(result.RootElement, "access_token", "Microsoft"),
            RequiredString(result.RootElement, "refresh_token", "Microsoft"), cancellationToken).ConfigureAwait(false);
    }

    private async Task<MicrosoftMinecraftSession> ExchangeForMinecraftAsync(string microsoftToken, string refreshToken, CancellationToken cancellationToken)
    {
        using JsonDocument xbox = await PostJsonAsync(XboxUserUrl, new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftToken },
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT"
        }, "Xbox Live", cancellationToken).ConfigureAwait(false);
        string xboxToken = RequiredString(xbox.RootElement, "Token", "Xbox Live");
        string userHash = RequiredUserHash(xbox.RootElement, "Xbox Live");

        using JsonDocument xsts = await PostJsonAsync(XstsUrl, new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xboxToken } },
            RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT"
        }, "XSTS", cancellationToken).ConfigureAwait(false);
        string xstsToken = RequiredString(xsts.RootElement, "Token", "XSTS");
        string xstsHash = RequiredUserHash(xsts.RootElement, "XSTS");
        if (userHash != xstsHash) throw new MicrosoftMinecraftAuthException("XSTS", "user_mismatch", "Xbox 用户身份不一致.");

        using JsonDocument minecraft = await ExchangeXboxTokenForMinecraftAsync(xstsHash, xstsToken, cancellationToken).ConfigureAwait(false);
        string minecraftToken = RequiredString(minecraft.RootElement, "access_token", "Minecraft");
        int expiresIn = RequiredInt(minecraft.RootElement, "expires_in", "Minecraft");

        using JsonDocument entitlements = await GetBearerJsonAsync(EntitlementsUrl, minecraftToken, "Minecraft", cancellationToken).ConfigureAwait(false);
        if (!entitlements.RootElement.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            throw new MicrosoftMinecraftAuthException("Minecraft", "no_entitlement", "此账户没有可用的 Minecraft Java 版授权.");

        using JsonDocument profile = await GetBearerJsonAsync(ProfileUrl, minecraftToken, "Minecraft", cancellationToken).ConfigureAwait(false);
        string name = RequiredString(profile.RootElement, "name", "Minecraft");
        string id = RequiredString(profile.RootElement, "id", "Minecraft");
        if (!Guid.TryParseExact(id, "N", out Guid uuid))
            throw new MicrosoftMinecraftAuthException("Minecraft", "invalid_profile", "Minecraft 档案中的 UUID 无效.");
        return new MicrosoftMinecraftSession(name, uuid, minecraftToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn)), refreshToken);
    }

    private async Task<JsonDocument> ExchangeXboxTokenForMinecraftAsync(string userHash, string xstsToken, CancellationToken cancellationToken)
    {
        try
        {
            return await PostJsonAsync(MinecraftLoginUrl,
                new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" }, "Minecraft", cancellationToken).ConfigureAwait(false);
        }
        catch (MicrosoftMinecraftAuthException error) when (error.Code.Equals("FORBIDDEN", StringComparison.OrdinalIgnoreCase))
        {
            throw new MicrosoftMinecraftAuthException("Minecraft", "FORBIDDEN",
                "Minecraft 拒绝了此应用的登录请求 (HTTP 403). 请确认应用已获准使用 Minecraft API; 如果刚提交审核, 请等待审核结果.");
        }
    }

    private async Task<JsonDocument> PostFormAsync(Uri uri, Dictionary<string, string> form, string stage, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendWithConnectionRetryAsync(() =>
        {
            HttpRequestMessage request = new(HttpMethod.Post, uri);
            request.Content = new FormUrlEncodedContent(form);
            return request;
        }, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, stage, allowOAuthError: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> PostJsonAsync<T>(Uri uri, T value, string stage, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendWithConnectionRetryAsync(() =>
        {
            HttpRequestMessage request = new(HttpMethod.Post, uri);
            // Xbox Live 的字段名区分大小写; JsonContent 的 Web 默认选项会改成 camelCase. 
            request.Content = JsonContent.Create(value, options: XboxJsonOptions);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (uri == XboxUserUrl || uri == XstsUrl) request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
            return request;
        }, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, stage, allowOAuthError: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetBearerJsonAsync(Uri uri, string accessToken, string stage, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendWithConnectionRetryAsync(() =>
        {
            HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, stage, allowOAuthError: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithConnectionRetryAsync(Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        // 对连接故障和短暂的服务端响应有限重试, 每次都创建新的请求. 
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage request = createRequest();
            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (attempt < 2 && ShouldRetryResponse(response))
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 400 : 900), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException error) when (attempt < 2 &&
                error.HttpRequestError is HttpRequestError.SecureConnectionError or HttpRequestError.ConnectionError)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 400 : 900), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool ShouldRetryResponse(HttpResponseMessage response)
    {
        int status = (int)response.StatusCode;
        if (status is 408 or 429 || status >= 500) return true;
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        return response.IsSuccessStatusCode &&
            (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                || response.Content.Headers.ContentLength == 0);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, string stage, bool allowOAuthError, CancellationToken cancellationToken)
    {
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            string mediaType = response.Content.Headers.ContentType?.MediaType ?? "未提供";
            throw new MicrosoftMinecraftAuthException(stage, "invalid_response",
                $"{stage} 返回非 JSON 响应 (HTTP {(int)response.StatusCode}, Content-Type: {mediaType}, 长度: {json.Length}).");
        }
        if (!response.IsSuccessStatusCode && !allowOAuthError)
        {
            using (document)
            {
                string code = document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.String
                    ? error.GetString()! : response.StatusCode.ToString();
                throw new MicrosoftMinecraftAuthException(stage, code,
                    stage == "Microsoft" ? OAuthErrorMessage(code) : $"{stage} 请求失败 ({code}).");
            }
        }
        return document;
    }

    private static string RequiredString(JsonElement root, string property, string stage) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new MicrosoftMinecraftAuthException(stage, "invalid_response", $"{stage} 响应缺少 {property}.");

    private static int RequiredInt(JsonElement root, string property, string stage) =>
        root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int number)
            ? number : throw new MicrosoftMinecraftAuthException(stage, "invalid_response", $"{stage} 响应缺少 {property}.");

    private static string RequiredUserHash(JsonElement root, string stage) =>
        root.TryGetProperty("DisplayClaims", out JsonElement claims) && claims.TryGetProperty("xui", out JsonElement users)
        && users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0
            ? RequiredString(users[0], "uhs", stage)
            : throw new MicrosoftMinecraftAuthException(stage, "invalid_response", $"{stage} 响应缺少用户标识.");

    private static string OAuthErrorMessage(string code) => code switch
    {
        "authorization_declined" => "Microsoft 登录已被拒绝.",
        "expired_token" => "登录代码已过期, 请重新开始登录.",
        "bad_verification_code" => "登录代码无效, 请重新开始登录.",
        "invalid_client" => "Microsoft 应用 Client ID 无效或未允许公共客户端流.",
        _ => $"Microsoft 登录失败 ({code})."
    };

    private static void ValidateClientId(string clientId)
    {
        if (!Guid.TryParse(clientId, out _)) throw new ArgumentException("必须提供有效的 Microsoft 应用 Client ID.", nameof(clientId));
    }
}
