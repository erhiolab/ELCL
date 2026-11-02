# 会话文件日志

命名空间: `ErhioLab.Craft.Launcher.Core.Logging`

`SessionFileLogger` 将一次程序运行的日志写入单独文件. 调用方指定日志目录, Core 不查找应用目录或用户目录.

```csharp
using System;
using System.IO;
using ErhioLab.Craft.Launcher.Core.Logging;

string logDirectory = Path.Combine(Environment.CurrentDirectory, "logs");
using SessionFileLogger logger = new(logDirectory);

logger.Information("Download", "开始获取版本清单.");
logger.Warning("Download", "首选来源不可用.");
logger.Error("Download", "获取失败.");
```

| API | 行为 |
| --- | --- |
| `new SessionFileLogger(directoryPath)` | 将目录转为绝对路径并创建目录, 开始一次新会话. |
| `FilePath` | 本次日志文件的绝对路径. |
| `Debug` / `Information` / `Warning` / `Error` | 使用指定 `source` 和消息写入一条记录; `Error` 可附带异常. |
| `Write(level, source, message, exception)` | 统一入口; `LogLevel` 为 `Debug`、`Information`、`Warning`、`Error`. |
| `Dispose()` | 关闭文件句柄. |

构造新会话时, 同目录下的 `Log1.txt` 至 `Log4.txt` 依次后移, 旧 `Log5.txt` 删除, 当前会话写入新的 `Log1.txt`. 最多保留五次会话; 其他文件名不受影响. 轮换和新文件创建使用跨进程互斥锁. 如果锁等待超时、文件被持续占用或目录不可写, 构造函数会抛出异常.

每条记录立即刷新到文件, 内容包含本地时间与时区、级别、来源和消息. 换行转义为 `\r` / `\n`, 使每条记录只占一行:

```text
2026-10-29 15:11:54.125 +08:00 [Information] [Download] 开始获取版本清单.
```

同一实例的写入由内部锁串行化. `Dispose` 后再次写入会抛出异常. Core 不会自动记录其他模块的请求; 调用方负责选择值得记录的事件. ELCL 自己的初始化方式见[启动器集成](../launcher/README.md).
