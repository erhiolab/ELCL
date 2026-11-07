namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>由调用方提供的启动模板上下文; 核心不查找账户/读取设置或决定目录位置.</summary>
public sealed record MinecraftLaunchPlaceholderContext
{
    public string? LauncherName { get; init; }
    public string? LauncherVersion { get; init; }
    public string? LauncherDirectory { get; init; }
    public string? LauncherExecutablePath { get; init; }
    public string? CacheDirectory { get; init; }
    public string? JavaDirectory { get; init; }
    public string? GameDirectory { get; init; }
    public string? VersionDirectory { get; init; }
    public string? GameInstanceDirectory { get; init; }
    public string? VersionName { get; init; }
    public string? GameVersion { get; init; }
    public string? ForgeVersion { get; init; }
    public string? FabricVersion { get; init; }
    public string? NeoForgeVersion { get; init; }
    public string? QuiltVersion { get; init; }
    public bool HasLiteLoader { get; init; }
    public string? PlayerName { get; init; }
    public Guid? PlayerUuid { get; init; }
    public string? LoginType { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>额外的普通标记, 键不包含花括号; 按枚举顺序优先替换.</summary>
    public IReadOnlyDictionary<string, string?> AdditionalValues { get; init; } = new Dictionary<string, string?>();

    /// <summary>额外的 Minecraft ${name} 参数值; 不会自动读取令牌等登录凭据.</summary>
    public IReadOnlyDictionary<string, string?> NativeValues { get; init; } = new Dictionary<string, string?>();

    /// <summary>允许 {setup:name} 读取的值; 由调用方显式提供, 不访问真实配置文件.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } = new Dictionary<string, string?>();

    /// <summary>{variable:name:default} 使用的变量; 缺失变量使用默认值或空字符串.</summary>
    public IReadOnlyDictionary<string, string?> Variables { get; init; } = new Dictionary<string, string?>();
}
