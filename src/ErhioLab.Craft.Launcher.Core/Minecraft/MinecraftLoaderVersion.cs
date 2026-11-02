namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>当前支持查询版本列表的附加加载器.</summary>
public enum MinecraftLoaderKind
{
    /// <summary>Minecraft Forge.</summary>
    Forge,

    /// <summary>NeoForge.</summary>
    NeoForge,

    /// <summary>Fabric Loader.</summary>
    Fabric,

    /// <summary>OptiFine.</summary>
    OptiFine,

    /// <summary>Quilt Loader.</summary>
    Quilt,

    /// <summary>LiteLoader.</summary>
    LiteLoader
}

/// <summary>一个可供用户选择的加载器版本.</summary>
/// <param name="Version">显示和后续安装使用的版本标识.</param>
/// <param name="IsPreview">是否为预览或测试版本.</param>
/// <param name="ReleaseDate">来源明确给出的发布日期, 可为空.</param>
/// <param name="LastModifiedDate">来源给出的最后更新时间, 可为空, 不能当作发布日期.</param>
/// <param name="RequiredForgeVersion">OptiFine 标注的 Forge 版本; null 表示不支持与 Forge 搭配.</param>
/// <param name="BuildDate">来源给出的构建日期, 可为空.</param>
/// <param name="ReleaseTime">官方来源给出的精确发布时间, 可为空.</param>
public sealed record MinecraftLoaderVersion(
    string Version,
    bool IsPreview,
    DateOnly? ReleaseDate = null,
    DateOnly? LastModifiedDate = null,
    string? RequiredForgeVersion = null,
    DateOnly? BuildDate = null,
    DateTimeOffset? ReleaseTime = null
);

/// <summary>指定游戏版本可用的加载器版本列表.</summary>
/// <param name="Kind">加载器类别.</param>
/// <param name="GameVersion">查询时使用的 Minecraft 版本.</param>
/// <param name="Source">实际返回有效结果的来源.</param>
/// <param name="Versions">匹配该游戏版本的版本列表, 可以为空.</param>
public sealed record MinecraftLoaderVersionCatalog(
    MinecraftLoaderKind Kind,
    string GameVersion,
    MinecraftManifestSource Source,
    IReadOnlyList<MinecraftLoaderVersion> Versions
);
