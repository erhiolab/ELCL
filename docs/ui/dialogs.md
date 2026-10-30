# 启动器弹窗

客户端的 `LauncherDialogs` 提供 `Information`/`Warning`/`Error` 和 `ConfirmAsync` 四种入口. `Information` 用于说明或完成提示, 显示蓝灰色遮罩; `Warning` 用于尚未失败但需要注意的风险, 使用橙红色标题和较浅的暖红遮罩; `Error` 用于已经失败的操作, 使用鲜红色和更深的红色遮罩; `Danger` 用于不可逆操作前的确认, 使用深红遮罩/填色确认按钮, 并提供取消与确认两个选择. 卡片文字与背景使用当前主题资源. 主窗口内的弹窗常驻可视树, 打开时立即显示遮罩, 卡片和遮罩带有打开/关闭缓动. 弹窗期间页面不能操作, 标题栏仍可拖动窗口. 启动失败/主窗口尚未显示时, 弹窗独立居中显示.

调用示例:

```csharp
LauncherDialogs.Information("提示", "操作已完成.");
LauncherDialogs.Error("保存失败", exception.Message);

if (await LauncherDialogs.ConfirmAsync(
        "重置个性化设置",
        "是否要重置 个性化 的所有设置? 此操作不可逆.",
        LauncherDialogLevel.Danger,
        "重置"))
{
    ResetToDefaults();
}
```

`ConfirmAsync` 只有在用户按下确认按钮时返回 `true`, 取消和关闭都不会执行操作. 弹窗实现位于启动器项目, 不依赖 Core. 当前的目录初始化失败和外观设置保存失败也使用这套弹窗.

设置 > 调试中的卡片可以手动预览普通提示/警告/错误和危险确认弹窗. 预览不会修改配置, 危险确认的结果只写入日志.
