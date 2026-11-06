namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>启动器保存的游戏目录/显示名称和上次选择的版本目录.</summary>
public sealed record GameFolderSetting(
    string Path,
    string Name,
    string? NameKey = null,
    bool IsNameCustomized = false,
    string? LastSelectedVersionPath = null
);
