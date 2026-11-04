# 核心版本号

启动器和核心库各有自己的版本号。想在“关于”页面或日志里显示当前加载的 Core 版本，可以直接读取：

```csharp
using ErhioLab.Craft.Launcher.Core;

Console.WriteLine(CoreInfo.ProductName);
Console.WriteLine(CoreInfo.ShortName);
Console.WriteLine(CoreInfo.Version);
```

`ShortName` 是 `ELCLC`。版本号来自当前加载的 Core 程序集，不需要在你的项目里再手写一份。目前仓库中的 Core 项目版本为 `0.0.1`。
