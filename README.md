# ErhioLab Craft Launcher

ErhioLab Craft Launcher (ELCL) 是一个重新设计的 Minecraft 启动器.

> 为什么是重新设计: 24年我还写过一个 ECL.

## 图标资源

启动器窗口与可执行文件/文档站导航及首页使用仓库 [`logo/`](logo/) 中的 ELCL 标识. 启动器鸣谢页的第三方图标来源和本地资源说明见[鸣谢页头像来源](src/ErhioLab.Craft.Launcher/Assets/Credits/README.md).

## 贡献

### 项目结构

- `ErhioLab.Craft.Launcher.slnx`: Windows WPF 启动器, 以及它引用的核心项目.
- `ErhioLab.Craft.Launcher.Core.slnx`: 可独立开发和构建的跨平台核心.
- `src/ErhioLab.Craft.Launcher`: Windows UI 外壳, 目标框架为 `net10.0-windows`.
- `src/ErhioLab.Craft.Launcher.Core`: 不依赖 UI 的类库, 目标框架为 `net10.0`.

核心项目应保持跨平台: 不要在其中直接依赖 WPF, Windows 注册表, WMI, `cmd.exe` 或写死的 `java.exe`. 平台差异通过接口隔离, 并由启动器或单独的平台实现负责.

核心库的独立使用方法见 [ELCLC 文档](web/docs/core/README.md); 项目约定见 [AGENTS.md](AGENTS.md).

### 构建

需要安装 .NET 10 SDK:

```powershell
dotnet build .\ErhioLab.Craft.Launcher.slnx
dotnet build .\ErhioLab.Craft.Launcher.Core.slnx
```

在 JetBrains Rider 中选择共享运行配置 `Run ELCL`, 点击运行或调试即可. Rider 会先构建启动器, 并通过项目引用自动构建 Core.

### 文档站

文档站以 `web/docs/` 中的 Markdown 为内容源, 使用 VitePress 和 pnpm. 需要 Node.js 20 或更新版本, 在 `web/` 目录执行:

```powershell
pnpm install
pnpm dev
```

浏览器打开 `http://127.0.0.1:3000/`. 运行 `pnpm build` 可生成静态页面.

