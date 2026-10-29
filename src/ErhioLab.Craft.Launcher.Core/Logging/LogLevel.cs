namespace ErhioLab.Craft.Launcher.Core.Logging;

/// <summary>区分普通信息、调试信息和需要关注的问题.</summary>
public enum LogLevel
{
    /// <summary>调试信息.</summary>
    Debug,

    /// <summary>普通操作和状态信息.</summary>
    Information,

    /// <summary>可继续运行的异常情况.</summary>
    Warning,

    /// <summary>操作失败或未处理的异常.</summary>
    Error
}
