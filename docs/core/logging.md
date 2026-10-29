# ELCL 日志系统

## 路径与职责

WPF 启动器在 `App.OnStartup` 中创建 `AppContext.BaseDirectory/ELCL`. 这个位置是运行中的 EXE 所在目录; Rider 调试时通常位于 `bin/Debug/net10.0-windows`, 发布后则位于启动器安装目录. 启动器把 `ELCL/logs` 作为参数传给 Core, Core 不决定数据目录的位置, 也不依赖 WPF 或 Windows API.

`SessionFileLogger` 只管理传入目录里的 `Log1.txt` 到 `Log5.txt`. 每次构造新的日志会话时, 旧的 `Log5.txt` 被删除, 其余文件依次后移, 新的 `Log1.txt` 用于本次运行. 最多保留五次启动记录. 不会删除此目录中其他名称的文件. 多个启动器同时启动时, Core 会对轮换和新文件创建加跨进程锁.

## Core API

```csharp
using ErhioLab.Craft.Launcher.Core.Logging;

string logDirectory = Path.Combine(dataDirectory, "logs");
using SessionFileLogger logger = new(logDirectory);

logger.Information("Download", "开始下载版本清单.");
logger.Warning("Download", "镜像不可用, 即将切换来源.");
logger.Error("Download", "下载失败.", exception);
```

构造函数中的路径可以是相对或绝对路径, Core 会将其转为绝对路径并创建目录. `FilePath` 返回本次日志文件的绝对路径. `Write` 还支持 `Debug`/`Information`/`Warning`/`Error` 四个级别. 写入由内部锁串行化, 每条日志立即刷新到文件. `Dispose` 关闭文件句柄.

每条记录包含本地日期时间与时区、级别、来源和内容. 消息和异常中的换行会转义为 `\\n`, 使一条记录占一行. 例如:

```text
2026-10-29 15:11:54.125 +08:00 [Information] [App] 启动器启动, 数据目录: ...
```

## 客户端初始化与记录点

`App.OnStartup` 负责创建 `ELCL`, 初始化日志会话, 然后加载主窗口. 如果目录创建或日志初始化失败, 启动器显示错误并停止启动. `App.OnExit` 写入退出记录并释放日志. UI 线程/应用程序和后台任务的未处理异常会尝试写入日志; 日志失败不会掩盖原始异常.

当前界面在主页面与侧栏切换、选择登录方式、点击下载或版本选择入口、最小化/关闭窗口和窗口状态变化时记录日志. 悬停、鼠标移动和动画每帧不记录, 以免淹没有意义的操作. 后续 Core 模块应通过调用方传入的日志会话记录关键步骤和失败, 不在 Core 内查找客户端路径.
