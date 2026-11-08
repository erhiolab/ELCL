# ELCLC 核心库

想写自己的 Minecraft Java 版启动器，又不想从版本清单、Java 查找和文件下载开始造轮子？ELCLC 把这些工作整理成一个 .NET 10 类库。ELCL 桌面启动器也在使用它。

Core 只处理数据和文件。你传入网络客户端、保存路径和需要的参数；界面、用户设置、账户和游戏启动流程由你的程序决定。

## 现在能做什么

| 想做的事 | 从这里开始 |
| --- | --- |
| 列出原版游戏版本，首选来源失败时自动换源 | [游戏版本列表](versions.md) |
| 查询 Forge、NeoForge、Fabric、Quilt 等加载器 | [模组加载器版本](loaders.md) |
| 下载并安装模组加载器，执行客户端安装处理器 | [安装模组加载器](loader-installation.md) |
| 查找并验证本机 Java | [Java 发现](java.md) |
| 下载一个或一批文件，查看进度、取消任务 | [文件下载](downloader.md) |
| 读取中文 Minecraft Wiki 的版本页面 | [Minecraft Wiki](wiki.md) |
| 根据用户名计算离线玩家 UUID | [离线玩家 UUID](offline-accounts.md) |
| 替换启动标题、参数和命令中的玩家名、版本与路径标记 | [启动模板替换标记](launch-placeholders.md) |
| 解析版本继承、收集启动依赖、解压本地库并生成启动参数 | [准备启动 Minecraft](launch.md) |
| 创建或更新官方格式的安装配置，保留已有档案 | [启动器安装档案](launcher-profiles.md) |
| 登录并验证 Microsoft / Minecraft 账户 | [Microsoft 账户登录](microsoft-auth.md) |
| 写会话日志、保存 JSON | [日志](logging.md) · [JSON 存储](storage.md) |

第一次使用，直接看[快速开始](quick-start.md)。如果你在决定某项功能该放在 Core 还是自己的程序里，可以看 [Core 负责什么](architecture.md)。

::: tip 当前进度
Core 的版本号是 `0.0.1`。已提供原版文件下载计划、加载器安装、Microsoft 账户请求和游戏启动准备 API；下载任务调度、账户保存、进程生命周期和界面仍由调用方组织。
:::
