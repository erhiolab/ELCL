using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Mods;

/// <summary>随核心分发的百科条目映射, 平台身份优先于名称匹配.</summary>
internal static class McModTranslationIndex
{
    private sealed record Entry(int Id, string ChineseName, string EnglishName, string? CurseForgeSlug, string? ModrinthSlug);

    private sealed record Index(Dictionary<string, Entry> CurseForge, Dictionary<string, Entry> Modrinth, Dictionary<string, Entry> Names);

    private static readonly Lazy<Index> Data = new(Read);

    internal static McModTranslation? Find(string name, string? curseForgeSlug, string? modrinthSlug)
    {
        var index = Data.Value;
        Entry? entry = null;
        if (curseForgeSlug is { Length: > 0 }) index.CurseForge.TryGetValue(curseForgeSlug, out entry);
        if (entry is null && modrinthSlug is { Length: > 0 }) index.Modrinth.TryGetValue(modrinthSlug, out entry);
        // 已提供平台身份时, 不用相似名称把另一个项目误认成本项目.
        if (entry is null && curseForgeSlug is null && modrinthSlug is null) index.Names.TryGetValue(Normalize(name), out entry);
        return entry is null
            ? null
            : new McModTranslation(entry.ChineseName, entry.EnglishName,
                new Uri("https://www.mcmod.cn/class/" + entry.Id + ".html"), entry.CurseForgeSlug, entry.ModrinthSlug);
    }

    private static Index Read()
    {
        using var input = typeof(McModTranslationIndex).Assembly.GetManifestResourceStream("ErhioLab.Craft.Launcher.Core.Mods.Data.mcmod-index.json.gz") ?? throw new InvalidDataException("百科索引资源不存在.");
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        var entries = JsonSerializer.Deserialize<Entry[]>(zip) ?? throw new InvalidDataException("百科索引无法解析.");
        var cf = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var mr = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.CurseForgeSlug is { Length: > 0 } curseforge)
            {
                cf.TryAdd(curseforge, entry);
                names.TryAdd(curseforge, entry);
            }

            if (entry.ModrinthSlug is { Length: > 0 } modrinth)
            {
                mr.TryAdd(modrinth, entry);
                names.TryAdd(modrinth, entry);
            }

            if (entry.ChineseName.Length > 0) names.TryAdd(Normalize(entry.ChineseName), entry);
            if (entry.EnglishName.Length > 0) names.TryAdd(Normalize(entry.EnglishName), entry);
        }

        return new Index(cf, mr, names);
    }

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
