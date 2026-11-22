using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Core.Modpacks;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.SystemTools;
using ErhioLab.Craft.Launcher.Tasks;
using ErhioLab.Craft.Launcher.Views.Launch;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher;

public partial class MainWindow
{
    private bool _modpackImportBusy;
    private bool _modpackImportInitialized;
    private WindowsFileDropTarget? _modpackDrop;

    private bool IsFileDropSurfaceActive => PrimaryPageLayer.IsVisible
        && !AccountManagementPage.IsVisible && !TaskManagerPage.IsVisible && !DialogOverlay.IsVisible;

    private bool IsModpackDropPage => IsFileDropSurfaceActive
        && (ReferenceEquals(_activeRoute, _versionSelectionRoute) || _activeRoute?.Page is LaunchPage);

    internal static bool IsVersionFileDropAllowed(Window window) => window is MainWindow main && main.IsFileDropSurfaceActive
        && ReferenceEquals(main._activeRoute, main._versionSettingsRoute);

    private void InitializeModpackImport()
    {
        _modpackImportInitialized = true;
        AllowDrop = true;
        foreach (var page in new FrameworkElement[] { AccountManagementPage, TaskManagerPage, DialogOverlay })
            page.IsVisibleChanged += (_, _) => UpdateModpackDropTarget();
        Loaded += (_, _) =>
        {
            UpdateModpackDropTarget();
            App.DeliverModpackFiles(this);
            _ = ProcessAssociatedModpacksAsync();
        };
        Closed += (_, _) =>
        {
            _modpackDrop?.Dispose();
            ModpackInstallTasks.Imported -= ModpackImported;
        };
        VersionSelectionPage.ImportModpackRequested += () => _ = ImportModpackAsync();
        ModpackInstallTasks.Imported += ModpackImported;
    }

    private void ModpackImported(string directory)
    {
        App.Logger.Information("Modpacks", $"整合包导入完成: {directory}.");
        if (ReferenceEquals(_activeRoute, _versionSelectionRoute)) _ = VersionSelectionPage.ReloadAsync();
    }

    private void UpdateModpackDropTarget()
    {
        if (!_modpackImportInitialized) return;
        if (!IsModpackDropPage || !IsLoaded)
        {
            _modpackDrop?.Dispose();
            _modpackDrop = null;
            return;
        }

        // 等待导航更新可见区域, 再订阅窗口的共用拖放接收器.
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded || !IsModpackDropPage) return;
            _modpackDrop ??= WindowsFileDropTarget.Attach(this, AcceptModpack, ModpackFilesDropped, ModpackDropFailed, _ => IsModpackDropPage);
            _modpackDrop?.SetEnabled(!_modpackImportBusy);
        }, DispatcherPriority.ContextIdle);
    }

    private static bool IsPackFile(string path) => File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".mrpack";

    private static bool AcceptModpack(string[] files) => files.Length == 1 && IsPackFile(files[0]);

    private void ModpackFilesDropped(string[] files)
    {
        if (!IsModpackDropPage || _modpackImportBusy) return;
        if (!AcceptModpack(files))
        {
            LauncherToasts.Error(LocalizationManager.Get("modpack.dropOne"));
            App.Logger.Information("Modpacks", "忽略整合包拖入, 需要单个 ZIP 或 MRPACK 文件.");
            return;
        }

        _ = ImportModpackAsync(files[0]);
    }

    private static void ModpackDropFailed(Exception error)
    {
        App.Logger.Error("Modpacks", "整合包拖放接收失败.", error);
        LauncherToasts.Error(LocalizationManager.Get("modpack.dropFailed"));
    }

    internal async Task ImportModpackAsync(string? path = null)
    {
        if (_modpackImportBusy) return;
        _modpackImportBusy = true;
        _modpackDrop?.SetEnabled(false);
        try
        {
            if (path is null)
            {
                var picker = new OpenFileDialog { Filter = LocalizationManager.Get("modpack.filter"), CheckFileExists = true, Multiselect = false };
                if (picker.ShowDialog(this) != true) return;
                path = picker.FileName;
            }

            App.Logger.Information("Modpacks", $"读取整合包: {path}.");
            var pack = await ModpackArchive.ReadAsync(path);
            string gameDirectory = ModpackInstallTasks.CurrentGameDirectory;
            string? name = await ModpackInstallDialog.PromptVersionNameAsync(pack.Name, gameDirectory, pack.GameVersion);
            if (name is null)
            {
                App.Logger.Information("Modpacks", "用户取消整合包导入.");
                return;
            }

            ModpackInstallTasks.Start(pack, name, gameDirectory);
            LauncherToasts.Information(LocalizationManager.Get("modpack.queued"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            App.Logger.Error("Modpacks", $"无法导入整合包: {path}.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("modpack.failed"), error.Message));
        }
        finally
        {
            _modpackImportBusy = false;
            UpdateModpackDropTarget();
            _ = ProcessAssociatedModpacksAsync();
        }
    }
}
