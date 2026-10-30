namespace ErhioLab.Craft.Launcher.Dialogs;

/// <summary>启动器弹窗的提示等级.</summary>
internal enum LauncherDialogLevel
{
    /// <summary>操作说明或完成提示.</summary>
    Information,

    /// <summary>需要留意的风险, 操作尚未失败.</summary>
    Warning,

    /// <summary>操作已经失败.</summary>
    Error,

    /// <summary>执行不可逆操作前请求确认.</summary>
    Danger
}
