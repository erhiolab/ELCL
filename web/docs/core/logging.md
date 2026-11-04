# 写日志

`SessionFileLogger` 为每次运行创建一个日志文件。你给它目录，它负责写入和轮换，不会去猜应用的数据目录。

```csharp
using ErhioLab.Craft.Launcher.Core.Logging;

using var log = new SessionFileLogger("./logs");
log.Information("Download", "开始下载");
log.Warning("Download", "首选地址不可用");
log.Error("Download", "下载失败", error);
```

当前日志写在 `log.FilePath`。新会话使用 `Log1.txt`，旧日志依次后移，最多保留五次会话。每条记录带时间、级别和来源，写入后立即刷新到文件。

目录不可写、文件持续被占用或轮换失败时会抛出异常。Core 不会自动记录其他模块的操作；需要记录哪些事件，由你的程序决定。
