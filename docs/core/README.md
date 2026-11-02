# ErhioLab Craft Launcher Core (ELCLC)

ELCLC 是面向 .NET 10 的 Minecraft Java 版启动器通用类库. 它可以独立于 ELCL 的 WPF 界面使用, 当前提供版本清单、附加加载器版本查询、Wiki 文本查询、会话日志和 JSON 文件存储. 目标框架为 `net10.0`.

> 当前版本为 `0.0.1`. 核心尚未实现游戏文件下载、安装、账户登录或启动游戏. 取得版本列表不等于完成安装.

## 开始使用

1. 阅读[快速开始](quick-start.md), 在 .NET 10 项目中引用 Core 并运行第一个查询.
2. 按需查看下方模块的调用方法、返回值和失败行为.
3. 如果要接入 ELCL 桌面端, 再看[启动器集成说明](../launcher/README.md).

| 模块 | 文档 | 主要入口 |
| --- | --- | --- |
| Minecraft 版本清单 | [版本清单](versions.md) | `MinecraftVersionCatalogClient` |
| 附加加载器版本 | [加载器](loaders.md) | `MinecraftLoaderVersionClient`、`MinecraftLoaderCompatibility` |
| 中文 Minecraft Wiki | [Wiki](wiki.md) | `MinecraftWikiClient` |
| 会话日志 | [日志](logging.md) | `SessionFileLogger` |
| JSON 文件存储 | [存储](storage.md) | `JsonFileStore<T>` |
| 核心版本 | [版本信息](version.md) | `CoreInfo` |

[设计边界](architecture.md)说明调用方与 Core 各自负责的内容. 示例使用项目引用; 将来若以 DLL 或 NuGet 包分发, 调用 API 的方式相同, 但接入项目必须使用兼容 `net10.0` 的目标框架.
