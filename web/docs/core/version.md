# 核心版本号

命名空间：`ErhioLab.Craft.Launcher.Core`。

`CoreInfo` 提供当前加载程序集的名称和版本，适合“关于”页面、日志或故障报告。

## 读取 `CoreInfo`

```csharp
using ErhioLab.Craft.Launcher.Core;

Console.WriteLine(CoreInfo.ProductName);
Console.WriteLine(CoreInfo.ShortName);
Console.WriteLine(CoreInfo.Version);
```

| 属性 | 内容 |
| --- | --- |
| `ProductName` | `ErhioLab Craft Launcher Core`。 |
| `ShortName` | `ELCLC`。 |
| `Version` | 当前程序集的信息版本，缺失时回退到程序集版本。 |

这些是属性，不需要创建实例或调用方法。核心与启动器分别维护版本，展示核心版本时不要复用自己的应用版本号。
