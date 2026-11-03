# ELCL 启动器配置

## 文件位置与职责

客户端选择数据目录, 当前为程序目录下的 `ELCL`. 配置保存在 `ELCL/config.json`. Core 的 `JsonFileStore<T>` 只接收文件路径/序列化选项和调用方定义的数据类型, 不知道启动器配置的字段/默认值或校验规则, 也不依赖 WPF.

首次启动时客户端写入默认配置. 随后启动先读取并规范化配置, 再创建主窗口. 配置无法解析或读取时, 原文件保持不变, 本次运行使用默认值并写入日志. 用户再次修改设置时会保存新配置.

```json
{
  "Theme": "Day",
  "AccentColor": "#80CEFF",
  "WindowOpacity": 1,
  "JavaInstallations": [],
  "IgnoredJavaPaths": [],
  "VersionListSource": "OfficialFirst",
  "DownloadMaxConnections": 8,
  "DownloadSpeedLimitMiBPerSecond": 0
}
```

`Theme` 为 `Day` 或 `Night`. `AccentColor` 为 `#RRGGBB`. `WindowOpacity` 为 `0.40` 到 `1.00`. `VersionListSource` 为 `OfficialFirst` 或 `MirrorFirst`, 仅指定首选来源, 另一来源仍会作为备用. 这些字段以及 `LauncherSettings.Normalize()` 都由启动器项目定义; 不认识的枚举值/颜色恢复默认值, 超出范围的不透明度会被限制到范围内. 启动器传入枚举序列化规则, 保持已有 JSON 格式兼容.

`DownloadMaxConnections` 通过滑块设置, 范围是 `1` 到 `256`, 默认 `8`. `DownloadSpeedLimitMiBPerSecond` 的单位为 MiB/s, `0` 表示不限速. 下载设置在任务创建时读取, 已创建的任务继续使用原来的参数.

## 启动设置与 Java 列表

设置侧栏顶部的"启动"页保存按优先级排列的 `JavaInstallations`. 每项包含 `java.exe` 路径/版本号/主版本号和架构; 列表为空时启动器会在后台自动查找. 页面默认只显示优先级最高的 3 项, 可展开查看全部; 其余条目以 240 毫秒的高度缓动显示或收起, 无须在设置页内增加第二个滚动区域. 每项的排序/打开文件夹和移除按钮在鼠标悬浮时显示. 用户可以手动获取/选择 `java.exe` 添加/上下移动/移除或打开所在文件夹. 手动添加的 Java 会置顶. 当前还未实现游戏下载和游戏启动; 将来按列表顺序选择首个兼容目标 Minecraft 版本的 Java, 版本独立设置可以覆盖自动选择.

客户端从 Windows 注册表收集额外安装目录, 然后调用 Core 的 [Java 发现 API](../core/java.md). Core 读取 `PATH`/`JAVA_HOME`/`JDK_HOME`, 检查常见安装目录和已知 Java 附近的目录. 目录搜索有深度与数量上限, 不遍历磁盘根目录. 候选 `java.exe` 会在限时内运行 `-XshowSettings:properties -version` 验证并读取版本. 刷新保留用户已排好的顺序, 新发现的 Java 加在末尾. `IgnoredJavaPaths` 记录手动移除的路径, 刷新不会重新加入; 重置启动设置会清空列表和排除项, 随即重新查找.

设置侧栏的下载重置按钮将版本列表源/最大连接数和速度限制恢复默认值, 不会改变外观设置.

## 保存流程

页面调用客户端的 `App.UpdateSettings`, 客户端先校验设置, 再通过 Core 写入临时文件并替换正式文件, 成功后更新内存设置和 WPF 主题. Core 在替换文件遇到短暂占用时有限次重试; 持续失败仍会抛出异常. 客户端记录失败路径并提示用户, 当前界面恢复已保存的设置.

## 导入与导出

在"设置 → 其他"中可以将当前生效的完整配置导出为 JSON 文件, 默认文件名为当前日期和时间. 导入时先解析并规范化所选文件, 然后原子替换 `ELCL/config.json` 并重新读取验证. 文件读取/解析或保存失败时会显示错误, 不会重启. 导入成功后显示警告弹窗; 用户关闭弹窗后启动器自动重启, 让全部设置从新配置重新初始化.

