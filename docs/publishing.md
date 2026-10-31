# 构建与发布 ELCL

启动器和核心分别维护版本号. 启动器版本位于 `src/ErhioLab.Craft.Launcher/ErhioLab.Craft.Launcher.csproj`, 核心版本位于 `src/ErhioLab.Craft.Launcher.Core/ErhioLab.Craft.Launcher.Core.csproj`, 当前均为 `0.0.1`.

普通构建的输出统一位于仓库根目录的 `artifacts/build/<项目名>/<配置>/<目标框架>/`, 中间文件位于 `artifacts/obj/<项目名>/`. `Run ELCL` 使用 `artifacts/build/ErhioLab.Craft.Launcher/Debug/net10.0-windows/ErhioLab.Craft.Launcher.exe`. 两个项目的源代码目录不再产生新的 `bin` 或 `obj`.

在 Rider 中选择 `Publish ELCL` 并运行, 或执行 `tools/publish-elcl.ps1`, 即可连续生成两个 Windows x64 单文件 EXE. 发布需要 .NET 10 SDK.

| 发布模式 | 最终文件 | 目标电脑要求 |
| --- | --- | --- |
| 依赖运行时 | `artifacts/publish/ELCL-0.0.1-win-x64-framework-dependent.exe` | 安装 .NET 10 Windows Desktop Runtime x64 |
| 自带运行时 | `artifacts/publish/ELCL-0.0.1-win-x64-self-contained.exe` | 无需预装 .NET 运行时 |

发布阶段的临时输出平铺在 `artifacts/staging/publish/win-x64/`, 两个 EXE 均采用 `ELCL-版本号-win-x64-类型.exe` 命名. `artifacts` 已被 Git 忽略. 两种模式全部成功后, 发布脚本会把这两个文件复制到 `artifacts/publish/`.

启动器运行后会在 EXE 旁创建 `ELCL` 数据目录, 发布脚本不会删除其中的数据. 这些产物属于 Windows x64, 不适用于其他系统或 CPU 架构. 自带运行时版本启动时可能将捆绑的原生库提取到系统临时目录.
