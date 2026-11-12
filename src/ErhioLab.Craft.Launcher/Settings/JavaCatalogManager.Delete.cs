using System.IO;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Settings;

internal sealed partial class JavaCatalogManager
{
    private readonly HashSet<string> _deleting = new(StringComparer.OrdinalIgnoreCase);

    internal async Task DeleteInstalledAsync(LauncherJavaInstallation java)
    {
        if (!java.InstalledByLauncher || !_deleting.Add(java.Path)) return;
        bool entered = false;
        try
        {
            string directory = ValidateManagedDirectory(java);
            RequireUnused(directory);
            if (!await LauncherDialogs.ConfirmAsync(L("java.deleteTitle"), string.Format(L("java.deletePrompt"), java.Version, directory),
                    LauncherDialogLevel.Danger, L("common.delete"))) return;
            await _refreshGate.WaitAsync();
            entered = true;
            directory = ValidateManagedDirectory(java);
            RequireUnused(directory);
            App.Logger.Information("Java", $"开始删除 ELCL 安装的 Java: {directory}, 标识={java.InstallationId}.");
            await Task.Run(() => DeleteManagedFiles(java));
            var removed = App.Settings.JavaInstallations.Where(entry => IsWithin(entry.Path, directory)).ToArray();
            var list = App.Settings.JavaInstallations.Except(removed).ToArray();
            string[] ignored = [.. App.Settings.IgnoredJavaPaths, .. removed.Select(entry => entry.Path)];
            if (!App.UpdateSettings(App.Settings with { JavaInstallations = list, IgnoredJavaPaths = ignored }))
            {
                App.Logger.Warning("Java", $"Java 目录已删除, 但列表保存失败: {directory}.");
                LauncherToasts.Error(L("java.deleteConfigFailed"));
                return;
            }

            Changed?.Invoke();
            App.Logger.Information("Java", $"Java 目录及列表项已删除: {directory}.");
            LauncherToasts.Success(L("java.deleted"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException)
        {
            App.Logger.Error("Java", $"删除 Java 失败: {java.Path}.", error);
            LauncherToasts.Error(string.Format(L("java.deleteFailed"), error.Message));
        }
        finally
        {
            if (entered) _refreshGate.Release();
            _deleting.Remove(java.Path);
        }
    }

    private static string ValidateManagedDirectory(LauncherJavaInstallation java)
    {
        if (!java.InstalledByLauncher || java.InstallationDirectory is null || java.InstallationId is not { } id || id == Guid.Empty)
            throw new InvalidOperationException(L("java.notManaged"));
        string directory = Path.GetFullPath(java.InstallationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.GetParent(directory) is null || !string.Equals(Path.GetFullPath(java.Path), Path.Combine(directory, "bin", "java.exe"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L("java.notManaged"));
        if (!Directory.Exists(directory)) return directory;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException(L("java.notManaged"));
        var marker = JsonSerializer.Deserialize<ManagedJavaMarker>(File.ReadAllText(Path.Combine(directory, ManagedJavaMarker.FileName)));
        if (marker?.InstallationId != id || marker.Executable != "bin/java.exe") throw new InvalidOperationException(L("java.notManaged"));
        return directory;
    }

    private static void RequireUnused(string directory)
    {
        if (App.Tasks.Tasks.Any(task => task.IsActive && task.JavaExecutablePath is { } path && IsWithin(path, directory)))
            throw new InvalidOperationException(L("java.inUse"));
    }

    private static void DeleteManagedFiles(LauncherJavaInstallation java)
    {
        string directory = ValidateManagedDirectory(java);
        if (!Directory.Exists(directory)) return;
        // 先尝试独占打开二进制文件, 避免 Windows 正在使用 Java 时先删除其余运行文件.
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll"))
        {
            using var check = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        // 已验证精确的安装目录和目录内标识; 不删除父目录或普通手动添加的 Java.
        Directory.Delete(directory, true);
    }

    private static bool IsWithin(string path, string directory) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string L(string key) => LocalizationManager.Get(key);
}
