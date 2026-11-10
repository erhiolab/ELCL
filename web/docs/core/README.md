# ELCLC 核心库

ELCLC 是可单独使用的 .NET 10 类库，提供 Minecraft Java 版启动器常用的网络、文件和启动准备功能。ELCL 桌面端使用同一份核心。

你传入 `HttpClient`、路径和参数，核心返回数据、文件计划或执行结果。界面、设置、账户保存和进程管理由你的应用安排。

第一次接入，从[快速开始](quick-start.md)运行一个版本查询。已经知道要用哪个类，可以查[公开 API 索引](api.md)。

## 获取版本与安装游戏

| 要做什么 | 文档 |
| --- | --- |
| 获取官方或镜像版本清单，离线解析清单 | [游戏版本列表](versions.md) |
| 列出已安装版本，包括元数据损坏的目录 | [本地版本扫描](local-versions.md) |
| 重命名、查询引用或删除单个版本目录 | [版本目录管理](version-directories.md) |
| 查询六种加载器、补充日期、判断组合 | [模组加载器版本](loaders.md) |
| 生成并执行原版安装所需的下载计划 | [安装原版](vanilla-installation.md) |
| 安装 Forge、NeoForge、Fabric、Quilt、LiteLoader 或 OptiFine | [安装模组加载器](loader-installation.md) |

## 账户与启动

| 要做什么 | 文档 |
| --- | --- |
| 设备代码登录、刷新或验证会话 | [Microsoft 登录](microsoft-auth.md) |
| 计算离线 UUID | [离线玩家 UUID](offline-accounts.md) |
| 读取、下载和修改皮肤披风 | [皮肤与披风](skins.md) |
| 合并继承链、准备文件、生成 Java 参数 | [准备启动 Minecraft](launch.md) |
| 展开标题、参数和命令模板 | [替换标记](launch-placeholders.md) |
| 维护官方格式的安装档案 | [安装档案](launcher-profiles.md) |

## 通用工具

| 要做什么 | 文档 |
| --- | --- |
| 查找 Java 或验证一个可执行文件 | [Java 发现](java.md) |
| 多文件、多连接下载，报告进度或取消 | [下载器](downloader.md) |
| 查询 Wiki 摘要、正文和实际语言链接 | [Minecraft Wiki](wiki.md) |
| 写分级日志、轮换会话文件 | [日志](logging.md) |
| 在指定路径读写任意 JSON 对象 | [JSON 存储](storage.md) |
| 显示当前加载的核心库版本 | [核心版本号](version.md) |

## 接入前先知道

Core 不依赖 WPF，不读取 ELCL 的配置或账户文件，也不决定数据目录。安装计划可以配合自定义下载器使用；启动参数生成不会自动启动游戏。

目前通过源码项目引用接入。核心仍在开发，签名与行为以当前仓库为准；边界与错误处理约定见[接入约定](architecture.md)。
