# ELCLC 核心库

想写自己的 Minecraft Java 版启动器，又不想从版本清单、Java 查找和文件下载开始造轮子？ELCLC 把这些工作整理成一个 .NET 10 类库。ELCL 桌面启动器也在使用它。

Core 只处理数据和文件。你传入网络客户端、保存路径和需要的参数；界面、用户设置、账户和游戏启动流程由你的程序决定。

## 现在能做什么

| 想做的事 | 从这里开始 |
| --- | --- |
| 列出原版游戏版本，首选来源失败时自动换源 | [游戏版本列表](versions.md) |
| 查询 Forge、NeoForge、Fabric、Quilt 等加载器 | [模组加载器版本](loaders.md) |
| 查找并验证本机 Java | [Java 发现](java.md) |
| 下载一个或一批文件，查看进度、取消任务 | [文件下载](downloader.md) |
| 读取中文 Minecraft Wiki 的版本页面 | [Minecraft Wiki](wiki.md) |
| 根据用户名计算离线玩家 UUID | [离线玩家 UUID](offline-accounts.md) |
| 登录并验证 Microsoft / Minecraft 账户 | [Microsoft 账户登录](microsoft-auth.md) |
| 写会话日志、保存 JSON | [日志](logging.md) · [JSON 存储](storage.md) |

第一次使用，直接看[快速开始](quick-start.md)。如果你在决定某项功能该放在 Core 还是自己的程序里，可以看 [Core 负责什么](architecture.md)。

::: tip 当前进度
Core 的版本号是 `0.0.1`。它已有通用文件下载能力，但还不能自动收集并安装某个 Minecraft 版本所需的全部文件，也不负责账户登录或启动游戏。
:::
