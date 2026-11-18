using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Core.Minecraft;
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

    private bool IsModpackDropPage => !AccountManagementPage.IsVisible && !VersionSettingsPage.IsVisible && !TaskManagerPage.IsVisible && !DialogOverlay.IsVisible && (VersionSelectionPage.IsVisible || PageHost.Content is LaunchPage && LaunchNavigation.IsChecked == true);

    private void InitializeModpackImport()
    {
        _modpackImportInitialized = true;
        AllowDrop = true;
        AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(Modpack_DragOver));
        AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(Modpack_DragOver));
        AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(Modpack_Drop));
        foreach (var page in new FrameworkElement[] { VersionSelectionPage, VersionSettingsPage, AccountManagementPage, TaskManagerPage, DialogOverlay })
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
        if (VersionSelectionPage.IsVisible) _ = VersionSelectionPage.ReloadAsync();
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

        // 等待离开的子页面释放其原生拖放目标, 再接管窗口.
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded || !IsModpackDropPage) return;
            _modpackDrop ??= WindowsFileDropTarget.TryAttach(this, ModpackFilesDropped, ModpackDropFailed);
            _modpackDrop?.SetEnabled(!_modpackImportBusy);
        }, DispatcherPriority.ContextIdle);
    }

    private static bool IsPackFile(string path) => File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".mrpack";

    private void Modpack_DragOver(object sender, DragEventArgs e)
    {
        if (!IsModpackDropPage || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        try
        {
            if (!_modpackImportBusy && (e.AllowedEffects & DragDropEffects.Copy) != 0
                                    && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && IsPackFile(files[0]))
                e.Effects = DragDropEffects.Copy;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // 拖动中的数据暂不可读时保持禁止状态, 不按高频事件反复通知.
        }
    }

    private void Modpack_Drop(object sender, DragEventArgs e)
    {
        if (!IsModpackDropPage || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        try
        {
            if (!_modpackImportBusy && (e.AllowedEffects & DragDropEffects.Copy) != 0 && e.Data.GetData(DataFormats.FileDrop) is string[] files)
                ModpackFilesDropped(files);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            ModpackDropFailed(error);
        }
    }

    private void ModpackFilesDropped(string[] files)
    {
        if (!IsModpackDropPage || _modpackImportBusy) return;
        if (files.Length != 1 || !IsPackFile(files[0]))
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
            string gameDirectory = App.Settings.SelectedGameFolderPath ?? Path.Combine(AppContext.BaseDirectory, ".minecraft");
            string initial = string.Concat(pack.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.');
            initial = initial[..Math.Min(initial.Length, 128)];

            string? Validate(string value)
            {
                try
                {
                    MinecraftVanillaDownloadPlanner.ValidateVersionName(value);
                }
                catch (ArgumentException)
                {
                    return LocalizationManager.Get("versionSelector.invalidName");
                }

                string target = Path.Combine(gameDirectory, "versions", value);
                return Directory.Exists(target) || File.Exists(target) || App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, target, StringComparison.OrdinalIgnoreCase))
                    ? LocalizationManager.Get("install.versionExists")
                    : null;
            }

            string? name = await LauncherDialogs.InputAsync(LocalizationManager.Get("modpack.inputTitle"),
                string.Format(LocalizationManager.Get("modpack.inputMessage"), pack.Name, pack.GameVersion, gameDirectory), initial, Validate);
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
