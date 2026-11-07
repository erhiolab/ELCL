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

如果建立连接或 TLS 握手暂时失败，Core 会重新创建请求并最多再试两次；HTTP 408、429、5xx 或成功状态下意外返回 HTML／空响应也会有限重试。明确的授权失败不会重试。如果某一步仍返回非 JSON，异常会给出阶段、HTTP 状态、Content-Type 和响应长度，便于排查；异常不包含令牌或响应正文。

Xbox Live 请求使用服务要求的原始 JSON 字段名，例如 `Properties`、`RelyingParty` 和 `TokenType`。调用方无需自行构造这些请求。

Minecraft 令牌通过 `/authentication/login_with_xbox` 获取，请求中使用 `identityToken`。Microsoft 网页授权成功只说明用户允许了应用；新注册的应用还可能需要单独获得 Minecraft API 的使用许可。若这一步返回 `403 FORBIDDEN`，先检查应用审核状态，参见 [Minecraft 帮助中心的申请说明](https://help.minecraft.net/hc/en-us/articles/16254801392141) 和 [申请表](https://aka.ms/mce-reviewappid)。提交后需等待审核，Core 无法绕过服务端的拒绝。

Client ID 不是密码，但必须属于你自己的应用注册。访问令牌和刷新令牌是敏感数据，不要写进日志或公开配置。ELCLC 不保存这些令牌；调用方需要自行选择安全的存储方式。

如果请求设备代码时出现 `AADSTS70002` 或 `invalid_client`，请检查应用注册的“身份验证”：添加“移动和桌面应用程序”平台，并启用“允许公共客户端流”。修改注册后重新开始登录。

如果没有 Minecraft Java 版授权或档案，登录会失败并抛出 `MicrosoftMinecraftAuthException`。它的 `Stage` 和 `Code` 可用于显示错误；调用方取消时会抛出取消异常。

## 读取当前皮肤

登录后可用 Minecraft 访问令牌读取玩家档案中的当前皮肤，再下载原始 PNG：

```csharp
var skins = new MinecraftSkinClient(http);
MinecraftPlayerSkin? active = await skins.GetActiveSkinAsync(session.MinecraftAccessToken);
if (active is not null)
{
    byte[] png = await skins.DownloadSkinAsync(active);
    await File.WriteAllBytesAsync("skin.png", png);
}
```

`GetActiveSkinAsync` 只选择档案中状态为 `ACTIVE` 的皮肤；没有时返回 `null`。Minecraft 档案有时返回 `http://textures.minecraft.net` 地址，Core 会将这个官方纹理域名改用 HTTPS。读取档案或下载图片遇到短暂连接故障、HTTP 408、429、5xx 时会有限重试。`DownloadSkinAsync` 要求 HTTPS、限制文件大小为 2 MiB，并检查 PNG 文件签名。Core 返回原始图片字节，不指定缓存目录，也不生成界面头像；调用方可以自行缓存并按需要裁剪头部。

## 皮肤和披风管理

`GetAppearanceAsync` 一次读取当前皮肤及账户拥有的披风。每件披风包含 ID、名称、纹理地址和是否正在使用；没有启用披风时，所有 `IsActive` 都是 `false`。`DownloadCapeAsync` 返回披风 PNG 的原始字节。

```csharp
MinecraftPlayerAppearance appearance = await skins.GetAppearanceAsync(session.MinecraftAccessToken);
foreach (MinecraftPlayerCape cape in appearance.Capes)
    Console.WriteLine($"{cape.Alias}: {(cape.IsActive ? "使用中" : "未使用")}");

byte[] png = await File.ReadAllBytesAsync("my-skin.png");
await skins.UploadSkinAsync(session.MinecraftAccessToken, png, "SLIM");

MinecraftPlayerCape? chosen = appearance.Capes.FirstOrDefault();
await skins.SetActiveCapeAsync(session.MinecraftAccessToken, chosen?.Id);
```

上传皮肤使用 Minecraft 档案接口，`variant` 只能是 `CLASSIC` 或 `SLIM`。上传前应检查图片尺寸；ELCLC 只检查 PNG 签名和 2 MiB 大小限制，最终是否接受由服务端决定。修改现有皮肤的模型需要将皮肤 PNG 以新 `variant` 重新上传。调用 `SetActiveCapeAsync` 时应传入当前账户拥有的披风 ID，服务端会校验所有权；传入 `null` 会取消装备披风。操作成功后重新读取档案，才能拿到最新的启用状态。

这些方法只发请求，不保存令牌、皮肤或披风，也不处理预览和文件选择。令牌刷新、图片缓存与界面由调用方负责。
