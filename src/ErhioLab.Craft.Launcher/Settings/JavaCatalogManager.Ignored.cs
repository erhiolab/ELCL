using System.ComponentModel;
using System.IO;

using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Settings;

internal sealed partial class JavaCatalogManager
{
    /// <summary>取消单个路径或所有路径的忽略状态, 检查后将可用 Java 追加到列表.</summary>
    internal async Task RestoreIgnoredAsync(string? path = null)
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            LauncherToasts.Information(LocalizationManager.Get("java.busy"));
            return;
        }

        try
        {
            string[] paths = IgnoredPaths.Where(ignored => path is null || SamePath(ignored, path)).ToArray();
            if (paths.Length == 0) return;
            IsRefreshing = true;
            Changed?.Invoke();
            App.Logger.Information("Java", $"开始取消忽略并检查 Java: {string.Join(", ", paths)}.");
            LauncherToasts.Information(LocalizationManager.Get("java.restoringIgnored"));

            var checkedPaths = new JavaInstallation?[paths.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, paths.Length), new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (index, cancellationToken) => checkedPaths[index] = await JavaDiscovery.ProbeAsync(paths[index], cancellationToken));
            List<LauncherJavaInstallation> list = Installations.ToList();
            HashSet<string> known = new(list.Select(java => java.Path), StringComparer.OrdinalIgnoreCase);
            HashSet<string> ignoredNow = new(IgnoredPaths, StringComparer.OrdinalIgnoreCase);
            int added = 0;
            for (int index = 0; index < paths.Length; index++)
            {
                if (ignoredNow.Contains(paths[index]) && checkedPaths[index] is { } java && known.Add(java.Path))
                {
                    list.Add(LauncherJavaInstallation.FromJava(java));
                    added++;
                }
            }

            string[] ignoredPaths = IgnoredPaths.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray();
            if (!Save(_data with { JavaInstallations = list, IgnoredJavaPaths = ignoredPaths })) return;
            App.Logger.Information("Java", $"取消忽略 Java 完成: {paths.Length} 个路径, 加入 {added} 个可用 Java.");
            LauncherToasts.Success(string.Format(LocalizationManager.Get("java.ignoredRestored"), paths.Length, added));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            App.Logger.Error("Java", "取消忽略 Java 失败.", error);
            LauncherToasts.Error(LocalizationManager.Get("java.restoreIgnoredFailed"));
        }
        finally
        {
            IsRefreshing = false;
            Changed?.Invoke();
            _refreshGate.Release();
        }
    }
}
