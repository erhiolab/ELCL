# Minecraft 版本列表和 Wiki 更新日志

`MinecraftVersionCatalogClient` 位于 Core, 只负责获取和解析 Java 版清单. 启动器从自身配置读取 `VersionListSource`, 决定先尝试官方源还是 BMCLAPI 镜像; Core 不读取启动器配置, 也不依赖 WPF.

```csharp
using HttpClient httpClient = new();
MinecraftVersionCatalogClient client = new(httpClient);
MinecraftVersionCatalog catalog = await client.GetCatalogAsync(MinecraftManifestSource.Official);
```

单个来源的请求默认限时 5 秒. 超时、HTTP 错误或无效清单会切换到另一来源. 两个来源均失败时抛出 `MinecraftVersionCatalogException`, 其 `InnerException` 保留两次失败的原因. 调用方负责记录日志和显示通知. `Parse` 可对已有 JSON 独立解析, 无需联网.

清单依据 Mojang 的 `type` 分为正式版、预览版和远古版. 愚人节版通常在清单中也标成 `snapshot`, 因此使用已知版本 ID 与 4 月 1 日的发布时间单独归类. 最新正式版和最新预览版使用清单 `latest` 字段, 不靠列表位置猜测. 版本条目保留元数据 JSON 地址、发布时间和可用时的 SHA-1, 为后续下载功能留出依据.

`MinecraftWikiClient` 使用中文 Minecraft Wiki 的 MediaWiki API. `GetSummaryAsync` 获取页面开头的纯文本, 用于版本行右侧 Wiki 按钮的悬停预览; `GetChangelogAsync` 可获取页面完整纯文本, 供后续需要内置阅读器的客户端使用. 当前点击 Wiki 按钮会在浏览器打开该版本页面. 页面不存在时返回 `null`, 网络或响应格式错误时抛出异常. Wiki 内容与 Mojang 版本清单来自不同服务, 不影响版本列表获取.

下载页的原版游戏侧栏条目悬停时会显示刷新图标. 获取版本列表开始、成功或失败时均使用通知条提示, 页面不显示状态文字. 点击版本行会选中该版本, 当前阶段只提示下载功能尚未提供.

参考资料: [Mojang 版本清单](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json), [PCL2 版本清单请求实现](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Modules/Minecraft/ModDownload.vb), [中文 Minecraft Wiki API](https://zh.minecraft.wiki/api.php).
