using System.IO;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Storage;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>本机 Java 清单, 不参与通用启动器配置的导入和导出.</summary>
internal sealed record JavaCatalogData
{
    public IReadOnlyList<LauncherJavaInstallation> JavaInstallations { get; init; } = [];
    public IReadOnlyList<string> IgnoredJavaPaths { get; init; } = [];

    internal JavaCatalogData Normalize() => this with
    {
        JavaInstallations = (JavaInstallations ?? []).Where(java => java is not null && !string.IsNullOrWhiteSpace(java.Path))
        .DistinctBy(java => java.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
        IgnoredJavaPaths = (IgnoredJavaPaths ?? []).Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

/// <summary>读取并原子保存本机 Java 清单.</summary>
internal sealed class JavaCatalogStore(string filePath)
{
    private readonly JsonFileStore<JavaCatalogData> _store = new(filePath);

    internal string FilePath => _store.FilePath;

    internal JavaCatalogData Load()
    {
        if (File.Exists(FilePath)) return Read(FilePath).Normalize();
        JavaCatalogData data = new();
        Save(data);
        return data;
    }

    internal void Save(JavaCatalogData data) => _store.Save(data.Normalize());

    private static JavaCatalogData Read(string path) => JsonSerializer.Deserialize<JavaCatalogData>(File.ReadAllText(path))
                                                        ?? throw new JsonException($"Java 列表内容不能为 null: {path}.");
}
