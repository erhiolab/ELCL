using System.Diagnostics.CodeAnalysis;

namespace ErhioLab.Craft.Launcher.Core.Mods;

public enum ModPlatform
{
    All,
    CurseForge,
    Modrinth
}

public enum CommunitySource
{
    OfficialFirst,
    MirrorFirst
}

public enum ModSortOrder
{
    Relevance,
    Downloads
}

public sealed record ModCategory(string Id, string ModrinthTag, int? CurseForgeId = null);

public sealed record ModSearchRequest(
    string Query = "",
    ModPlatform Platform = ModPlatform.All,
    string? GameVersion = null,
    string? Loader = null,
    ModCategory? Category = null,
    int Page = 0,
    int PageSize = 20,
    ModSortOrder SortOrder = ModSortOrder.Relevance
);

public sealed record ModProject(
    ModPlatform Platform,
    string Id,
    string Slug,
    string Name,
    string Summary,
    Uri Website,
    Uri? Icon,
    long Downloads,
    DateTimeOffset? Updated,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<string> Categories,
    bool CanDownload = true
);

public sealed record ModSearchResult(IReadOnlyList<ModProject> Projects, long Total, bool HasMore, IReadOnlyList<ModPlatform> FailedPlatforms)
{
    public IReadOnlyDictionary<ModPlatform, Exception> SourceErrors { get; init; } = new Dictionary<ModPlatform, Exception>();
}

public sealed record ModDependency(string? ProjectId, string? VersionId);

public sealed record ModFile(
    ModPlatform Platform,
    string ProjectId,
    string Id,
    string Name,
    string FileName,
    Uri? DownloadUri,
    long Size,
    string? Sha1,
    string? Sha512,
    DateTimeOffset? Published,
    string ReleaseType,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<ModDependency> RequiredDependencies
);

public sealed record ModFilePage(IReadOnlyList<ModFile> Files, bool HasMore);

public sealed record ModDownloadItem(ModProject Project, ModFile File);

[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global", Justification = "Matched platform identifiers are part of the public library API.")]
public sealed record McModTranslation(string ChineseName, string EnglishName, Uri Website, string? CurseForgeSlug, string? ModrinthSlug);

[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global", Justification = "Local mod metadata is part of the public library API.")]
public sealed record LocalModInfo(
    string Path,
    string FileName,
    string Name,
    string? Version,
    string Description,
    IReadOnlyList<string> ModIds,
    IReadOnlyList<string> Loaders,
    bool Enabled,
    string Sha1,
    uint CurseForgeFingerprint,
    byte[]? Icon = null,
    string? Error = null
);
