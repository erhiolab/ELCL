# 主题与配色规范

## 当前主题

- `Day`: 白昼模式, 浅色背景与深色文字.
- `Night`: 深夜模式, 深色背景与浅色文字.
- 默认主题为 `Day`.
- 主题色默认 `#80CEFF`.
- 个性化页使用可复用的 `AccentColorPicker` 控件, 在弹层中选择预设色/调整色相、饱和度和亮度, 或输入六位 RGB 色值. HEX 与拉条双向同步, 确认后立即保存并应用.

`LauncherThemeManager` 负责把启动器定义的外观配置映射为 WPF 色彩. 明暗模式变化时只替换 WPF UI 的主题资源字典, 不调用会更新窗口背景的 `ApplicationThemeManager.Apply`; 主题色变化只替换 `LauncherTheme.xaml` 中的共享画刷. 不透明度变化只更新内容层. 页面使用 `DynamicResource` 引用这些画刷, 切换主题时现有视图会立即更新. 新页面应复用这些画刷, 避免在控件上写死明暗相关颜色.

用户切换白昼/深夜模式时, `MainWindow` 会在更新主题资源前截取旧内容, 再以 350 毫秒淡出截图. 截图位于 `WindowRoot` 内, 因此跟随同一窗口透明度和圆角裁剪; 启动时加载配置不执行过渡动画.

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

## 界面组件

`App.xaml` 按顺序加载启动器画刷、缓动、按钮、导航、列表和表单资源. 新页面优先使用以下共享资源:

| 资源 | 用途 |
| --- | --- |
| `LauncherCard` | 设置页与工具页的标题、分隔线和内容容器; 标题可以是文本或自定义布局 |
| `PanelCardStyle` | 不需要标准标题的卡片容器 |
| `PrimaryActionStyle` / `SecondaryActionStyle` | 主次操作按钮 |
| `VersionActionStyle` / `DetailsBackButtonStyle` / `CreditLinkButtonStyle` | 图标操作、返回与链接按钮 |
| `VersionListRowStyle` / `InsetListRowStyle` / `VersionListActionsStyle` | 版本、Java 等列表行及悬停操作组 |
| `PlainListBoxItemStyle` / `SelectableListRowStyle` | 无边框的版本列表容器及带选中反馈的加载器列表行 |
| `FormTextBoxStyle` / `FormComboBoxStyle` / `BareTextBoxStyle` | 表单输入与由外层容器绘制边框的输入框 |

颜色定义保留在 `LauncherTheme.xaml`, 通用动画在 `MotionStyles.xaml`, 其余样式按控件类别放在 `ButtonStyles.xaml`、`NavigationStyles.xaml`、`ListStyles.xaml` 和 `FormStyles.xaml`. 页面资源只放该页面独有的布局或状态样式. 新的列表或按钮应先复用现有样式, 再通过局部尺寸属性调整, 以保持悬停和按压反馈一致.
