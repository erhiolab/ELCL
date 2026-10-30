# 启动器配置

## 文件位置与职责

客户端选择数据目录, 当前为程序目录下的 `ELCL`. 配置保存在 `ELCL/config.json`. Core 的 `JsonFileStore<T>` 只接收文件路径/序列化选项和调用方定义的数据类型, 不知道启动器配置的字段、默认值或校验规则, 也不依赖 WPF.

首次启动时客户端写入默认配置. 随后启动先读取并规范化配置, 再创建主窗口. 配置无法解析或读取时, 原文件保持不变, 本次运行使用默认值并写入日志. 用户再次修改设置时会保存新配置.

```json
{
  "Theme": "Day",
  "AccentColor": "#80CEFF",
  "WindowOpacity": 1,
  "VersionListSource": "OfficialFirst"
}
```

`Theme` 为 `Day` 或 `Night`. `AccentColor` 为 `#RRGGBB`. `WindowOpacity` 为 `0.40` 到 `1.00`. `VersionListSource` 为 `OfficialFirst` 或 `MirrorFirst`, 仅指定首选来源, 另一来源仍会作为备用. 这些字段以及 `LauncherSettings.Normalize()` 都由启动器项目定义; 不认识的枚举值/颜色恢复默认值, 超出范围的不透明度会被限制到范围内. 启动器传入枚举序列化规则, 保持已有 JSON 格式兼容.

设置侧栏的下载重置按钮只将 `VersionListSource` 恢复为 `OfficialFirst`, 不会改变外观设置.

## 保存流程

页面调用客户端的 `App.UpdateSettings`, 客户端先校验设置, 再通过 Core 写入临时文件并替换正式文件, 成功后更新内存设置和 WPF 主题. Core 在替换文件遇到短暂占用时有限次重试; 持续失败仍会抛出异常. 客户端记录失败路径并提示用户, 当前界面恢复已保存的设置.
