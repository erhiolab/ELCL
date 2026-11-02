# ELCL 启动器集成说明

这里记录 ELCL Windows 客户端如何接入 Core. 这些路径、配置字段和界面行为属于 ELCL, 不构成通用核心库的 API 合约.

- [启动器配置](settings.md): `ELCL/config.json`、外观与下载来源设置.
- [下载页面](download.md): 版本列表、附加加载器选择及通知.

启动器目标框架为 `net10.0-windows`, 使用 WPF. 启动时它在可执行文件旁创建 `ELCL` 数据目录, 把其中的 `logs` 路径传给 `SessionFileLogger`, 并通过 `JsonFileStore<LauncherSettings>` 保存自己的设置. Core 不会自行选择这些路径或读取 WPF 页面.

启动器记录初始化、页面切换、版本查询和失败等事件; 鼠标移动与动画每帧不记录. 日志初始化失败时客户端显示错误并停止启动. 核心日志的轮换规则与调用方法见[会话文件日志](../core/logging.md).
