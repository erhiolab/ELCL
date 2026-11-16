using System.IO;

using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Settings;

internal sealed partial class JavaCatalogManager
{
    private JavaCatalogStore? _store;
    private JavaCatalogData _data = new();

    internal IReadOnlyList<LauncherJavaInstallation> Installations => _data.JavaInstallations;
    internal IReadOnlyList<string> IgnoredPaths => _data.IgnoredJavaPaths;

    internal void Load(string path)
    {
        bool existing = File.Exists(path);
        App.Logger.Information("Java", $"开始读取 Java 列表: {path}, 独立文件已存在={existing}.");
        var store = new JavaCatalogStore(path);
        JavaCatalogData data = store.Load();
        _store = store;
        _data = data;
        App.Logger.Information("Java", $"Java 列表已{(existing ? "加载" : "建立")}: {Installations.Count} 个, 已忽略 {IgnoredPaths.Count} 个路径.");
    }

    private bool Save(JavaCatalogData data)
    {
        JavaCatalogData normalized = data.Normalize();
        try
        {
            (_store ?? throw new InvalidOperationException("Java 列表尚未初始化.")).Save(normalized);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.Logger.Error("Java", $"保存 Java 列表失败: {_store?.FilePath}.", error);
            LauncherToasts.Error(LocalizationManager.Get("java.saveFailed"));
            return false;
        }

        _data = normalized;
        return true;
    }
}
