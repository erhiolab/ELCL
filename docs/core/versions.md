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

下载页的原版游戏侧栏条目悬停时会显示刷新图标. 获取游戏版本列表开始、成功或失败时使用通知条提示. 点击版本行进入下载详情页, 返回后保留版本列表和搜索内容.

## 附加加载器版本

`MinecraftLoaderVersionClient` 位于 Core, 按 Minecraft 版本查询 Forge、NeoForge、Fabric、Quilt、LiteLoader 和 OptiFine. 调用方提供 `HttpClient`、游戏版本与优先来源; Core 不读取启动器设置, 也不引用 WPF.

```csharp
using HttpClient httpClient = new();
MinecraftLoaderVersionClient client = new(httpClient);
MinecraftLoaderVersionCatalog catalog = await client.GetVersionsAsync(
    MinecraftLoaderKind.Forge,
    "1.21.1",
    MinecraftManifestSource.Official);
```

每个来源默认限时 8 秒. 优先来源失败或没有匹配版本时, 再尝试另一来源. 两个来源都有效但均没有匹配项时, 返回空列表, 详情页显示“无”. 两个来源都失败时抛出 `MinecraftLoaderVersionException`, 其中保留两次错误. `Source` 表示实际取得非空列表的来源; 空列表返回最先成功解析的来源.

| 加载器 | 官方来源 | 镜像来源 | 解析方式 |
| --- | --- | --- | --- |
| Forge | [Forge Maven 元数据](https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml) | [BMCLAPI Forge](https://bmclapi2.bangbang93.com/forge/minecraft/1.21.1) | 按游戏版本前缀或 `mcversion` 筛选 |
| NeoForge | [NeoForge Maven 元数据](https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml) | [BMCLAPI NeoForge](https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge) | 将游戏版本映射到 Maven 版本前缀; 1.20.1 使用旧坐标 |
| Fabric | [Fabric Meta API](https://meta.fabricmc.net/v2/versions/loader/1.21.1) | [BMCLAPI Fabric Meta](https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.21.1) | 读取指定游戏版本的 `loader.version` |
| Quilt | [Quilt Meta API](https://meta.quiltmc.org/v3/versions/loader/1.21.1) | [BMCLAPI Quilt Meta](https://bmclapi2.bangbang93.com/quilt-meta/v3/versions/loader/1.21.1) | 读取指定游戏版本的 `loader.version`; 当前镜像接口可能返回 404, 会回退官方源 |
| LiteLoader | [LiteLoader 官方元数据](https://dl.liteloader.com/versions/versions.json) | [BMCLAPI LiteLoader](https://bmclapi2.bangbang93.com/liteloader/list?mcversion=1.12.2) | 官方 `artefacts` / `snapshots` 或镜像列表; 按游戏版本筛选 |
| OptiFine | [OptiFine 下载页](https://optifine.net/downloads) | [BMCLAPI OptiFine](https://bmclapi2.bangbang93.com/optifine/versionList) | 按游戏版本筛选文件名或 JSON 条目 |

Fabric 对不支持的游戏版本可能由官方接口返回 400、镜像返回 404. 这两种响应作为空列表处理, 例如 Minecraft 1.12.2 不会显示获取失败. 其他 HTTP 错误仍按来源失败处理并尝试回退.

详情页的六个分组分别显示“正在获取...”“可以添加”“无”“无兼容版本”或“获取失败, 点击重试”. 失败分组可单独重试; 有版本时可展开、选择版本或选择“无”清除. 目前只获取与选择版本号, “开始下载”仍未安装任何文件. 实际安装前还需要处理文件校验和版本目录命名.

`MinecraftLoaderCompatibility.CanCombine` 是不依赖界面的组合判断. 当前只允许 OptiFine 单独选择, 或与符合其 `RequiredForgeVersion` 标注的 Forge 版本搭配; OptiFine 标注 `N/A` 时不能与 Forge 搭配. 对 `1.13` 到 `1.14.3` 的 Forge + OptiFine 组合做额外限制. Forge、NeoForge、Fabric、Quilt、LiteLoader 之间目前互斥; 选择另一种加载器会清除不兼容的旧选择. Forge 与 OptiFine 彼此只展示兼容版本. PCL2 和 Prism Launcher 的某些旧版本还支持 Forge + LiteLoader, 但 ELCL 尚未实现该组合的安装流程, 因此先保守限制.

加载器列表项显示版本号和日期. OptiFine 官方页面提供发布日期和 Forge 版本要求; 镜像列表也提供 Forge 版本要求. Forge 镜像提供 `modified` 更新时间, 因此界面分别标为“发布于”和“更新于”. 官方 Forge 列表先显示版本, 随后通过 `GetForgeVersionDatesAsync` 异步补充镜像日期; 补充失败不会隐藏版本. LiteLoader 提供构建时间. 其他来源未提供逐版本日期时, 界面显示“发布时间未提供”, 不根据版本号推测日期.

来源回退、NeoForge 版本映射及 OptiFine 的 Forge 兼容规则参考了 [PCL2 的下载页面](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Pages/PageDownload/PageDownloadInstall.xaml.vb) 和 [版本解析](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Modules/Minecraft/ModDownload.vb). [Prism Launcher 的元数据更新脚本](https://github.com/PrismLauncher/meta/blob/main/update.sh)用于核对 Quilt 与 LiteLoader 来源. 解析和网络访问由本项目的 Core 独立实现.

参考资料: [Mojang 版本清单](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json), [PCL2 版本清单请求实现](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Modules/Minecraft/ModDownload.vb), [中文 Minecraft Wiki API](https://zh.minecraft.wiki/api.php).
