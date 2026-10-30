namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>Mojang 清单中的版本类别.</summary>
public enum MinecraftVersionKind
{
    Release,
    Preview,
    Ancient,
    AprilFools
}

/// <summary>版本清单的来源.</summary>
public enum MinecraftManifestSource
{
    Official,
    Mirror
}

/// <summary>清单中可用于后续下载版本元数据的条目.</summary>
/// <param name="Id">Mojang 版本标识.</param>
/// <param name="Kind">界面分类.</param>
/// <param name="MetadataUrl">该版本元数据 JSON 的地址.</param>
/// <param name="ReleaseTime">发布时间.</param>
/// <param name="Sha1">元数据的 SHA-1, 旧清单可能没有.</param>
public sealed record MinecraftVersion(
    string Id,
    MinecraftVersionKind Kind,
    Uri MetadataUrl,
    DateTimeOffset ReleaseTime,
    string? Sha1
);

/// <summary>一次成功获取并解析的版本清单.</summary>
/// <param name="Source">实际使用的来源.</param>
/// <param name="LatestReleaseId">官方标记的最新正式版.</param>
/// <param name="LatestPreviewId">官方标记的最新预览版.</param>
/// <param name="Versions">按清单原有顺序排列的版本.</param>
public sealed record MinecraftVersionCatalog(
    MinecraftManifestSource Source,
    string LatestReleaseId,
    string LatestPreviewId,
    IReadOnlyList<MinecraftVersion> Versions
);
