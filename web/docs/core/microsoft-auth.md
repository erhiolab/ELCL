# Microsoft 账户登录

命名空间：`ErhioLab.Craft.Launcher.Core.Accounts`。

使用设备代码流登录，依次交换 Microsoft、Xbox Live、XSTS 和 Minecraft 会话，验证 Java 版授权并读取玩家档案。

核心不打开网页、不复制代码、不保存账户，也不提供内置 Client ID。

## 创建 `MicrosoftMinecraftAuthClient`

先注册允许个人 Microsoft 账户和公共客户端流的应用，并确认 Minecraft API 的使用许可。设备代码流不要求客户端密钥。

```csharp
using ErhioLab.Craft.Launcher.Core.Accounts;

using var http = new HttpClient();
var client = new MicrosoftMinecraftAuthClient(http);
string clientId = Environment.GetEnvironmentVariable("MY_APP_CLIENT_ID")
    ?? throw new InvalidOperationException("请配置自己的应用 Client ID。");
CancellationToken cancellationToken = default;
```

环境变量名称仅是示例，值的读取方式由你的应用决定。

## `MicrosoftMinecraftAuthClient.StartDeviceAuthorizationAsync` {#microsoftminecraftauthclient-startdeviceauthorizationasync}

请求一组设备代码。

```csharp
var authorization = await client.StartDeviceAuthorizationAsync(clientId, cancellationToken);
Console.WriteLine($"打开 {authorization.VerificationUri}");
Console.WriteLine($"输入代码：{authorization.UserCode}");
```

返回 `MicrosoftDeviceAuthorization`：

| 字段 | 用途 |
| --- | --- |
| `UserCode`、`VerificationUri` | 展示给用户的代码与网页。 |
| `DeviceCode` | 后续轮询使用，不是供用户输入的代码。 |
| `ExpiresAt`、`PollInterval` | 过期时间与服务要求的轮询间隔。 |

调用方可以打开网页并复制 `UserCode`，再继续下一步。应用注册错误或服务拒绝会抛出认证异常。

## `MicrosoftMinecraftAuthClient.CompleteDeviceAuthorizationAsync` {#microsoftminecraftauthclient-completedeviceauthorizationasync}

等待网页授权，并完成 Minecraft 会话交换。

```csharp
MicrosoftMinecraftSession session = await client.CompleteDeviceAuthorizationAsync(
    clientId, authorization, cancellationToken);
Console.WriteLine($"已登录：{session.UserName}");
```

`authorization` 必须来自同一应用的设备代码请求。方法按服务间隔轮询，处理等待授权和要求减慢轮询的响应，不需要调用方再写循环。

代码过期、用户拒绝、服务拒绝或缺少 Java 版授权时会失败；取消令牌可停止等待。

## `MicrosoftMinecraftAuthClient.RefreshAsync` {#microsoftminecraftauthclient-refreshasync}

使用之前安全保存的 Microsoft 刷新令牌取得新会话。

```csharp
var renewed = await client.RefreshAsync(
    clientId, session.MicrosoftRefreshToken, cancellationToken);
```

成功后应保存这次返回的新刷新令牌、Minecraft 访问令牌和过期时间，而不是一直复用旧结果。

方法同样验证 Java 版授权与档案。刷新失败时，由调用方决定是否提示重新登录；不会自动打开浏览器。

## `MicrosoftMinecraftAuthClient.ValidateMinecraftSessionAsync` {#microsoftminecraftauthclient-validateminecraftsessionasync}

验证当前 Minecraft 令牌，返回最新玩家名和 UUID。

```csharp
MinecraftAccountProfile profile = await client.ValidateMinecraftSessionAsync(
    renewed.MinecraftAccessToken, cancellationToken);
```

只验证传入的 Minecraft 访问令牌，不会读取存储，也不会自动刷新。过期后可以调用 `RefreshAsync` 再验证；`no_entitlement` 表示没有可用 Java 版授权。

## 会话返回值

`MicrosoftMinecraftSession` 包含 `UserName`、`Uuid`、`MinecraftAccessToken`、`MinecraftAccessTokenExpiresAt` 和 `MicrosoftRefreshToken`。

Client ID 是公开应用标识；令牌则是凭据。不要将它们一起放进公开配置、日志或错误截图。核心不负责加密保存，调用方应使用平台提供的凭据保护方式。

皮肤和披风使用 Minecraft 令牌，见[外观 API](skins.md)。

## 错误怎么处理

`MicrosoftMinecraftAuthException.Stage` 表示失败阶段，`Code` 表示服务或核心错误码，适合用于决定提示；`Message` 是可读说明。

| 现象 | 检查方向 |
| --- | --- |
| `invalid_client`、`AADSTS70002` | 应用是否支持个人账户，是否添加桌面平台并允许公共客户端流。 |
| Microsoft 网页成功，Minecraft 阶段返回 403 | 应用是否获准访问 Minecraft API，不能只以网页成功判断完成。 |
| `no_entitlement` | 账户没有可用 Java 版授权。 |
| Xbox／XSTS 阶段拒绝 | 检查该阶段错误，可能与账户或家庭限制有关。 |
| 主动取消 | 单独处理 `OperationCanceledException`，不当成登录故障。 |

短暂连接故障、HTTP 408、429、5xx 和部分异常响应会有限重试，明确授权失败不会重试。非 JSON 响应的错误会包含阶段、状态与长度，不附带完整响应正文。

应用许可说明见 [Minecraft 帮助中心](https://help.minecraft.net/hc/en-us/articles/16254801392141) 与[申请入口](https://aka.ms/mce-reviewappid)。核心无法绕过服务端的应用审核。
