# 皮肤与披风

命名空间：`ErhioLab.Craft.Launcher.Core.Accounts`。

读取官方玩家档案，下载原始 PNG，或修改这个账户的外观。不生成头像或三维模型，不决定缓存目录。

## 创建 `MinecraftSkinClient`

以下示例共用 HTTP 客户端和有效的 Minecraft 访问令牌，不是 Microsoft 刷新令牌。

```csharp
using ErhioLab.Craft.Launcher.Core.Accounts;

using var http = new HttpClient();
var client = new MinecraftSkinClient(http);
string accessToken = Environment.GetEnvironmentVariable("MY_MINECRAFT_TOKEN")
    ?? throw new InvalidOperationException("请先通过登录 API 获取有效会话。");
CancellationToken cancellationToken = default;
```

生产应用通常从自己保存的[登录会话](microsoft-auth.md)读取令牌，示例环境变量不是核心约定。

## `MinecraftSkinClient.GetActiveSkinAsync` {#minecraftskinclient-getactiveskinasync}

只读取当前启用的皮肤。

```csharp
MinecraftPlayerSkin? skin = await client.GetActiveSkinAsync(accessToken, cancellationToken);
```

没有 `ACTIVE` 皮肤时返回 `null`。结果包含纹理 `Url` 与可为空的模型 `Variant`，不包含图片字节。

## `MinecraftSkinClient.GetAppearanceAsync` {#minecraftskinclient-getappearanceasync}

一次取得当前皮肤与这个账户拥有的披风。

```csharp
MinecraftPlayerAppearance appearance = await client.GetAppearanceAsync(
    accessToken, cancellationToken);
foreach (var cape in appearance.Capes)
    Console.WriteLine($"{cape.Alias}：{cape.IsActive}");
```

`ActiveSkin` 可为空。披风包含 `Id`、`Alias`、`Url` 和 `IsActive`；没有装备时，所有条目的活动状态都是 `false`。

## `MinecraftSkinClient.DownloadSkinAsync` {#minecraftskinclient-downloadskinasync}

```csharp
if (skin is not null)
{
    byte[] png = await client.DownloadSkinAsync(skin, cancellationToken);
    await File.WriteAllBytesAsync("skin.png", png, cancellationToken);
}
```

输入 `MinecraftPlayerSkin`，返回原始 PNG 字节。要求 HTTPS、大小不超过 2 MiB，并验证 PNG 签名。

官方档案返回的 `http://textures.minecraft.net` 会转换成 HTTPS。核心不裁剪头像，保存与渲染由调用方处理。

## `MinecraftSkinClient.DownloadCapeAsync` {#minecraftskinclient-downloadcapeasync}

```csharp
MinecraftPlayerCape? chosen = appearance.Capes.FirstOrDefault();
if (chosen is not null)
{
    byte[] capePng = await client.DownloadCapeAsync(chosen, cancellationToken);
    await File.WriteAllBytesAsync("cape.png", capePng, cancellationToken);
}
```

输入披风对象，使用相同的 HTTPS、大小与 PNG 检查。需要缩略图或鞘翅模型时，由界面层解释纹理布局。

## `MinecraftSkinClient.UploadSkinAsync` {#minecraftskinclient-uploadskinasync}

上传并修改账户的实际皮肤。

```csharp
byte[] upload = await File.ReadAllBytesAsync("my-skin.png", cancellationToken);
await client.UploadSkinAsync(accessToken, upload, "SLIM", cancellationToken);
```

`png` 是图片字节，`variant` 只能为 `CLASSIC` 或 `SLIM`。修改模型也需要将当前 PNG 以新模型重新上传。

核心检查 PNG 签名与 2 MiB 上限，不解码验证像素尺寸。调用方应先做尺寸检查，服务端会决定是否接受。

## `MinecraftSkinClient.SetActiveCapeAsync` {#minecraftskinclient-setactivecapeasync}

```csharp
if (chosen is not null)
    await client.SetActiveCapeAsync(accessToken, chosen.Id, cancellationToken);

// 取消装备披风。
await client.SetActiveCapeAsync(accessToken, null, cancellationToken);
```

非空 ID 必须属于该账户，传入 `null` 取消装备。选择不存在或无权使用的 ID 会被服务拒绝。

操作完成后重新调用 `GetAppearanceAsync` 获取最新状态，核心不会修改调用方先前拿到的对象。

## 网络与错误

请求遇到短暂网络故障或 HTTP 408、429、5xx 时有限重试。服务拒绝、PNG 不符合要求或档案数据损坏时抛出相应异常；主动取消使用取消异常。

令牌刷新、图片缓存与错误通知由调用方安排，不会因为下载纹理就自动重新登录。
