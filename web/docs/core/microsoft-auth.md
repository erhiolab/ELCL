# Microsoft 账户登录

`MicrosoftMinecraftAuthClient` 负责发送登录请求，依次取得 Microsoft、Xbox Live、XSTS 和 Minecraft 会话，再读取玩家的 Java 版档案。Core 不决定 Client ID、不打开浏览器，也不保存账户或令牌；这些由调用它的程序负责。

## 设备代码登录

先在 Microsoft Entra 注册支持个人 Microsoft 账户的应用，并允许公共客户端流。把该应用的 Client ID 传给 Core：

```csharp
using ErhioLab.Craft.Launcher.Core.Accounts;

using HttpClient http = new();
var client = new MicrosoftMinecraftAuthClient(http);
string clientId = "你的应用 Client ID";

var request = await client.StartDeviceAuthorizationAsync(clientId);
Console.WriteLine($"打开 {request.VerificationUri}，输入 {request.UserCode}");

var session = await client.CompleteDeviceAuthorizationAsync(clientId, request);
Console.WriteLine($"已登录：{session.UserName} ({session.Uuid})");
```

调用方可以复制用户代码并打开 `VerificationUri`。`CompleteDeviceAuthorizationAsync` 会按服务器给定的间隔轮询，处理等待授权和减慢轮询的响应，直到成功、过期、失败或调用方取消。成功后返回 Minecraft 用户名、UUID、游戏访问令牌及过期时间、Microsoft 刷新令牌。刷新时调用 `RefreshAsync(clientId, refreshToken)`，并保存新返回的刷新令牌。

如果建立连接或 TLS 握手暂时失败，Core 会重新创建请求并最多再试两次；HTTP 408、429、5xx 或成功状态下意外返回 HTML／空响应也会有限重试。明确的授权失败不会重试。如果某一步仍返回非 JSON，异常会给出阶段、HTTP 状态、Content-Type 和响应长度，便于排查；异常和日志不会包含令牌或响应正文。

Xbox Live 请求使用服务要求的原始 JSON 字段名，例如 `Properties`、`RelyingParty` 和 `TokenType`。调用方无需自行构造这些请求。

Minecraft 令牌通过 `/authentication/login_with_xbox` 获取，请求中使用 `identityToken`。Microsoft 网页授权成功只说明用户允许了应用；新注册的应用还可能需要单独获得 Minecraft API 的使用许可。若这一步返回 `403 FORBIDDEN`，先检查应用审核状态，参见 [Minecraft 帮助中心的申请说明](https://help.minecraft.net/hc/en-us/articles/16254801392141) 和 [申请表](https://aka.ms/mce-reviewappid)。提交后需等待审核，Core 无法绕过服务端的拒绝。

Client ID 不是密码，但必须属于你自己的应用注册。访问令牌和刷新令牌是敏感数据，不要写进日志或公开配置。ELCL 桌面端把 Client ID 等服务配置放在 `ELCL/services.json`，环境变量 `ELCL_MICROSOFT_CLIENT_ID` 可覆盖文件值；账户令牌由 Windows 当前用户的 DPAPI 保护后保存到独立账户文件。该文件复制到另一 Windows 用户时，受保护的令牌无法直接解密。

如果请求设备代码时出现 `AADSTS70002` 或 `invalid_client`，请检查应用注册的“身份验证”：添加“移动和桌面应用程序”平台，并启用“允许公共客户端流”。修改注册后重新开始登录。服务配置文件或环境变量修改后，也需要重启 ELCL 才会重新读取。

如果没有 Minecraft Java 版授权或档案，登录会失败并抛出 `MicrosoftMinecraftAuthException`。它的 `Stage` 和 `Code` 可用于显示错误；调用方取消时会抛出取消异常。
