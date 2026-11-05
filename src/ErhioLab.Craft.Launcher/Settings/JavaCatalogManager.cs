using System.IO;

using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>在客户端配置中维护 Java 列表与用户指定的优先顺序.</summary>
internal sealed class JavaCatalogManager
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    internal event Action? Changed;

    internal bool IsRefreshing { get; private set; }

    internal Task InitializeIfEmptyAsync() => App.Settings.JavaInstallations.Count == 0 ? RefreshAsync(false) : Task.CompletedTask;

    internal async Task RefreshAsync(bool showNotification)
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        IsRefreshing = true;
        Changed?.Invoke();
        App.Logger.Information("Java", "开始查找本机 Java.");
        if (showNotification)
        {
        LauncherToasts.Information(LocalizationManager.Get("java.finding"));
        }

        try
        {
            IReadOnlyList<JavaInstallation> found = await Task.Run(() => JavaDiscovery.FindAsync(WindowsJavaSearchLocations.GetRoots()));
            JavaInstallation[] previous = App.Settings.JavaInstallations.ToArray();
            JavaInstallation?[] checkedPrevious = await Task.WhenAll(previous.Select(java => JavaDiscovery.ProbeAsync(java.Path)));
            Dictionary<string, JavaInstallation> validPrevious = checkedPrevious.OfType<JavaInstallation>()
                .ToDictionary(java => java.Path, StringComparer.OrdinalIgnoreCase);
            // 后台探测期间可能发生手动添加或排序, 提交前以最新列表为准.
            JavaInstallation[] current = App.Settings.JavaInstallations.ToArray();
            HashSet<string> ignored = new(App.Settings.IgnoredJavaPaths, StringComparer.OrdinalIgnoreCase);
            List<JavaInstallation> result = current
                .Select(java => validPrevious.GetValueOrDefault(java.Path) ?? (previous.Any(old => SamePath(old.Path, java.Path)) ? null : java))
                .OfType<JavaInstallation>().ToList();
            HashSet<string> paths = new(result.Select(java => java.Path), StringComparer.OrdinalIgnoreCase);
            foreach (JavaInstallation java in found)
            {
                if (!ignored.Contains(java.Path) && paths.Add(java.Path))
                {
                    result.Add(java);
                }
            }

            if (App.UpdateSettings(App.Settings with { JavaInstallations = result }))
            {
                int added = result.Count(java => !current.Any(old => SamePath(old.Path, java.Path)));
                App.Logger.Information("Java", $"Java 搜索完成, 共 {result.Count} 个, 新发现 {added} 个.");
                if (showNotification)
                {
                if (result.Count > 0) LauncherToasts.Success(string.Format(LocalizationManager.Get("java.found"), result.Count));
                else LauncherToasts.Error(LocalizationManager.Get("java.noneFound"));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            App.Logger.Error("Java", "查找 Java 失败.", error);
                LauncherToasts.Error(LocalizationManager.Get("java.findFailed"));
        }
        finally
        {
            IsRefreshing = false;
            Changed?.Invoke();
            _refreshGate.Release();
        }
    }

    internal async Task AddAsync(string path)
    {
        JavaInstallation? java = await JavaDiscovery.ProbeAsync(path);
        if (java is null)
        {
            App.Logger.Warning("Java", $"手动添加的 Java 无法通过验证: {path}.");
            LauncherToasts.Error(LocalizationManager.Get("java.invalid"));
            return;
        }

        List<JavaInstallation> list = [java, .. App.Settings.JavaInstallations.Where(existing => !SamePath(existing.Path, java.Path))];
        string[] ignored = App.Settings.IgnoredJavaPaths.Where(ignoredPath => !SamePath(ignoredPath, java.Path)).ToArray();
        if (App.UpdateSettings(App.Settings with { JavaInstallations = list, IgnoredJavaPaths = ignored }))
        {
            Changed?.Invoke();
            App.Logger.Information("Java", $"手动添加 Java: {java.Path}.");
        LauncherToasts.Success(string.Format(LocalizationManager.Get("java.added"), java.Version));
        }
    }

    internal void Move(JavaInstallation java, int offset)
    {
        List<JavaInstallation> list = App.Settings.JavaInstallations.ToList();
        int index = list.FindIndex(entry => SamePath(entry.Path, java.Path));
        int next = index + offset;
        if (index < 0 || next < 0 || next >= list.Count)
        {
            return;
        }

        (list[index], list[next]) = (list[next], list[index]);
        if (App.UpdateSettings(App.Settings with { JavaInstallations = list }))
        {
            Changed?.Invoke();
            App.Logger.Information("Java", $"调整 Java 顺序: {java.Path}, {index + 1} -> {next + 1}.");
        }
    }

    internal void Remove(JavaInstallation java)
    {
        List<JavaInstallation> list = App.Settings.JavaInstallations.Where(entry => !SamePath(entry.Path, java.Path)).ToList();
        string[] ignored = [.. App.Settings.IgnoredJavaPaths, java.Path];
        if (App.UpdateSettings(App.Settings with { JavaInstallations = list, IgnoredJavaPaths = ignored }))
        {
            Changed?.Invoke();
            App.Logger.Information("Java", $"从列表移除 Java: {java.Path}.");
        LauncherToasts.Success(LocalizationManager.Get("java.removed"));
        }
    }

    internal async Task ResetAsync()
    {
        if (!App.UpdateSettings(App.Settings with { JavaInstallations = [], IgnoredJavaPaths = [] }))
        {
            return;
        }

        App.Logger.Information("Java", "Java 列表已重置, 开始重新搜索.");
        Changed?.Invoke();
        await RefreshAsync(true);
    }

    /// <summary>按用户顺序选择首个符合游戏要求的 Java; 版本独立设置可另行覆盖.</summary>
    internal JavaInstallation? FirstCompatible(int minimumMajor, int maximumMajor = int.MaxValue) =>
        App.Settings.JavaInstallations.FirstOrDefault(java => java.MajorVersion >= minimumMajor && java.MajorVersion <= maximumMajor);

    private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
