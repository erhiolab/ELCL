# 主题与配色规范

## 当前主题

- `Day`: 白昼模式, 浅色背景与深色文字.
- `Night`: 深夜模式, 深色背景与浅色文字.
- 默认主题为 `Day`.
- 主题色默认 `#80CEFF`.
- 个性化页使用可复用的 `AccentColorPicker` 控件, 在弹层中选择预设色/调整色相、饱和度和亮度, 或输入六位 RGB 色值. HEX 与拉条双向同步, 确认后立即保存并应用.

`LauncherThemeManager` 负责把启动器定义的外观配置映射为 WPF 色彩. 明暗模式变化时只替换 WPF UI 的主题资源字典, 不调用会更新窗口背景的 `ApplicationThemeManager.Apply`; 主题色变化只替换 `LauncherTheme.xaml` 中的共享画刷. 不透明度变化只更新内容层. 页面使用 `DynamicResource` 引用这些画刷, 切换主题时现有视图会立即更新. 新页面应复用这些画刷, 避免在控件上写死明暗相关颜色.

## 共享画刷

| 资源 | 用途 |
| --- | --- |
| `InkBrush` | 主窗口与页面背景 |
| `InkRaisedBrush` | 标题栏和侧栏背景 |
| `PanelBrush` | 卡片/内容块背景 |
| `PanelHoverBrush` | 常规悬停层 |
| `LineBrush` / `LineSoftBrush` | 常规边框 / 弱分隔线 |
| `TextBrush` / `TextMutedBrush` | 主要文字 / 辅助文字 |
| `AccentBrush` | 主题色图形/按钮/选中标记 |
| `AccentForegroundBrush` | 主题色按钮上的文字, 根据主题色明度选择深色或白色 |
| `AccentTextBrush` | 主题色文字; 白昼模式中会加深, 保持可读性 |
| `AccentMutedBrush` / `AccentHoverBrush` | 主题色低对比背景 / 悬停背景 |

新增主题时, 在启动器项目的 `Settings/LauncherTheme.cs` 添加值, 在 `LauncherThemeManager` 补齐整套资源映射, 然后扩展个性化页的选择项. 不透明度作用于透明窗口中的 `WindowRoot` 内容层, 不属于颜色资源. 拖动滑块时实时预览, 松开后保存一次; 键盘调整在短暂停顿后保存. WPF 窗口启用 `AllowsTransparency`, 内容在普通窗口状态按圆角裁剪, 最大化时恢复直角.
