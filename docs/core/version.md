# 核心版本信息

命名空间: `ErhioLab.Craft.Launcher.Core`

核心版本独立于任何接入它的客户端. 当前项目版本由 `src/ErhioLab.Craft.Launcher.Core/ErhioLab.Craft.Launcher.Core.csproj` 的 `Version` 属性定义, 当前为 `0.0.1`.

```csharp
using System;
using ErhioLab.Craft.Launcher.Core;

Console.WriteLine(CoreInfo.ProductName);
Console.WriteLine(CoreInfo.ShortName);
Console.WriteLine(CoreInfo.Version);
```

| 属性 | 说明 |
| --- | --- |
| `CoreInfo.ProductName` | `ErhioLab Craft Launcher Core`. |
| `CoreInfo.ShortName` | `ELCLC`. |
| `CoreInfo.Version` | 从已加载 Core 程序集的信息版本读取, 取不到时回退到程序集版本, 再回退为 `unknown`. |

调用方可以在诊断信息中同时记录自身版本与 `CoreInfo.Version`, 不需要复制一个可能过期的核心版本常量.
