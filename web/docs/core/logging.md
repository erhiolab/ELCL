# 日志

命名空间：`ErhioLab.Craft.Launcher.Core.Logging`。

`SessionFileLogger` 在调用方指定的目录中写会话日志，不会自动收集其他核心模块的事件。

## 创建 `SessionFileLogger`

```csharp
using ErhioLab.Craft.Launcher.Core.Logging;

using var log = new SessionFileLogger("logs");
Console.WriteLine(log.FilePath);
```

构造时创建目录、轮换旧日志并开始写 `Log1.txt`。保留最近五次会话，新会话会把旧文件编号后移。

目录不可写、轮换失败或文件持续被占用时会抛出异常。每条日志有本地时间、级别与来源，写后立即刷新。

## `SessionFileLogger.Debug` {#sessionfilelogger-debug}

记录诊断细节。

```csharp
log.Debug("Versions", "开始解析缓存清单。");
```

## `SessionFileLogger.Information` {#sessionfilelogger-information}

记录操作开始、完成或一般状态。

```csharp
log.Information("Download", "开始下载游戏文件。");
```

## `SessionFileLogger.Warning` {#sessionfilelogger-warning}

记录仍可继续的异常状态。

```csharp
log.Warning("Download", "首选来源失败，将尝试备用来源。");
```

## `SessionFileLogger.Error` {#sessionfilelogger-error}

记录失败，可附加完整异常。

```csharp
var error = new IOException("示例读取失败。");
log.Error("Storage", "无法读取设置，已保留原文件。", error);
```

## `SessionFileLogger.Write` {#sessionfilelogger-write}

需要动态选择级别时使用统一入口。

```csharp
log.Write(LogLevel.Information, "Game", "进程已退出。");
```

`level` 为级别，`source` 为非空模块名称，`message` 为内容，`exception` 可省略。消息与异常中的换行会转义，避免一条记录拆成多行。

## `SessionFileLogger.Dispose` {#sessionfilelogger-dispose}

```csharp
log.Dispose();
```

刷新并关闭文件，重复释放没有影响；之后继续写入会抛出 `ObjectDisposedException`。通常使用页首的 `using` 自动释放即可。

## 记录前先脱敏

日志器不会识别令牌或密码。不要把完整启动命令、认证响应或敏感模板上下文直接传入，先由调用方过滤。
