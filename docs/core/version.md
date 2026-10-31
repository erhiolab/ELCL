# 核心版本

核心版本定义在 `src/ErhioLab.Craft.Launcher.Core/ErhioLab.Craft.Launcher.Core.csproj` 的 `Version` 属性中, 当前为 `0.0.1`. 它独立于启动器版本. 构建时 .NET SDK 将该值写入核心程序集的信息版本.

启动器可通过公共 API 读取当前加载的核心版本:

```csharp
using ErhioLab.Craft.Launcher.Core;

string coreVersion = CoreInfo.Version;
```

`CoreInfo.Version` 来自核心程序集本身, 无需在启动器中重复维护版本常量. 启动器当前在启动日志中记录该值.
