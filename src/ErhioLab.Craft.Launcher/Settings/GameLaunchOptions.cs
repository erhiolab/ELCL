namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>游戏进程的调度优先级, 待游戏启动时应用.</summary>
public enum GameProcessPriority
{
    High,
    Normal,
    Low
}

/// <summary>游戏窗口的初始显示方式, 待游戏启动时应用.</summary>
public enum GameWindowMode
{
    Fullscreen,
    Default,
    MatchLauncher,
    Custom,
    Maximized
}
