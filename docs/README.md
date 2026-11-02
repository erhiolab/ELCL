# 文档目录

## 面向核心库使用者

从 [ELCLC 核心库首页](core/README.md)开始. [快速开始](core/quick-start.md)提供项目引用和第一个版本查询示例; 各模块页面列出方法、参数、返回值、失败行为与数据来源.

| 模块 | 文档 |
| --- | --- |
| 设计边界 | [Core 架构](core/architecture.md) |
| Minecraft 版本清单 | [版本清单](core/versions.md) |
| Forge / Fabric / Quilt 等加载器 | [附加加载器](core/loaders.md) |
| 中文 Minecraft Wiki | [Wiki](core/wiki.md) |
| 会话文件日志 | [日志](core/logging.md) |
| 通用 JSON 文件存储 | [存储](core/storage.md) |
| 核心版本 | [版本信息](core/version.md) |

## 面向 ELCL 开发者

- [启动器集成](launcher/README.md): 桌面端的数据目录、日志初始化和 Core 接入.
- [启动器配置](launcher/settings.md)与[下载页面](launcher/download.md): 客户端专属行为.
- [UI 文档](ui/visual-direction.md): 视觉方向、主题、通知与对话框.
- [代码风格](development/code-style.md)、[文档站开发](development/docs-site.md)与[构建发布](publishing.md).

核心库和启动器分别维护版本号. 目前核心库还未实现游戏文件下载、安装与启动; 文档仅描述已存在的 API.
