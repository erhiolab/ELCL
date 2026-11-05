# ErhioLab Craft Launcher 项目约定

## 文档位置

- 本仓库的文档站位于 `web/`，文档源文件位于 `web/docs/`。
- 修改 ELCLC 核心库的公开 API 或可观察行为时，同步更新 `web/docs/core/` 中对应的页面；修改网站首页时编辑 `web/docs/index.md`。
- 用户说“写文档”或“更新文档”时，优先在 `web/docs/` 完成。不要把仓库根目录的旧 `docs/` 路径或源码目录里的 README 当成文档站。
- 官网面向启动器用户，核心文档面向使用 ELCLC 的开发者。说明实际已实现的行为，不把预留界面写成已完成的功能。

## 项目边界与验证

- `src/ErhioLab.Craft.Launcher.Core` 是可复用的 .NET 类库；Minecraft 领域逻辑可以放在这里，WPF、启动器配置、主题、通知和数据目录选择留在 `src/ErhioLab.Craft.Launcher`。
- WPF 界面优先复用 `src/ErhioLab.Craft.Launcher/Styles/` 的按钮、列表和卡片样式；新控件使用动态主题资源。展开、切页、悬浮及选中状态要有与现有界面一致的缓动。
- 启动器界面图标统一从 `src/ErhioLab.Craft.Launcher/Icons/LauncherIcons.cs` 取用. 新增图标前先查找并复用语义相同的现有图标; 确实没有时, 先在该文件定义并命名, 再在 XAML 或代码中引用. 避免在页面里直接写 `!`/Unicode 符号或重复的 Path 数据充当图标.
- 字体图标使用 `LauncherIcons.GlyphFont`, 矢量图标使用 `LauncherIcons` 中的 Geometry; 颜色使用主题动态资源, 保持深浅主题一致.
- 修改核心代码后构建核心与启动器；修改 `web/docs/` 后从 `web/` 运行 `pnpm build`，确认文档站可以生成。
- 用户要求检查 IDE 问题时，读取 Rider 的问题视图并逐项处理可解决的问题；报告构建和检查结果。
- 保留已有用户改动，不为了修一个问题顺手重置或清理其他文件。
