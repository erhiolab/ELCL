# ErhioLab Craft Launcher

ErhioLab Craft Launcher (ELCL) 是一个重新设计的 Minecraft 启动器.

> 为什么是重新设计: 24年我还写过一个 ECL.

## 贡献

### 项目结构

- `ErhioLab.Craft.Launcher.slnx`: Windows WPF 启动器, 以及它引用的核心项目.
- `ErhioLab.Craft.Launcher.Core.slnx`: 可独立开发和构建的跨平台核心.
- `src/ErhioLab.Craft.Launcher`: Windows UI 外壳, 目标框架为 `net10.0-windows`.
- `src/ErhioLab.Craft.Launcher.Core`: 不依赖 UI 的类库, 目标框架为 `net10.0`.

核心项目应保持跨平台: 不要在其中直接依赖 WPF, Windows 注册表, WMI, `cmd.exe` 或写死的 `java.exe`. 平台差异通过接口隔离, 并由启动器或单独的平台实现负责.

项目设计与 Core 规范在 [`docs`](docs/README.md) 中.

### 构建

需要安装 .NET 10 SDK:

```powershell
dotnet build .\ErhioLab.Craft.Launcher.slnx
dotnet build .\ErhioLab.Craft.Launcher.Core.slnx
```

在 JetBrains Rider 中选择共享运行配置 `Run ELCL`, 点击运行或调试即可. Rider 会先构建启动器, 并通过项目引用自动构建 Core.

