using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.SystemTools;
using ErhioLab.Craft.Launcher.Tasks;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Launch;

public partial class VersionWorldsPage
{
    private LocalMinecraftVersion? _version;
    private CancellationTokenSource? _scan;
    private WindowsFileDropTarget? _windowsDrop;
    private int _revision;
    private int _backupRequest;
    private WorldRow? _backupWorld;

    internal event Action<LocalMinecraftWorld>? DataPacksRequested;
    internal event Func<LocalMinecraftWorld, Task>? QuickPlayRequested;

    public VersionWorldsPage()
    {
        InitializeComponent();
        SelectionBar.LocalInfoRequested += () => WithSelectedWorld(OpenLocalInfo);
        SelectionBar.DeleteRequested += () => WithSelectedWorld(row => _ = DeleteAsync(row));
        SelectionBar.DeselectRequested += () => WorldList.SelectedItem = null;
        SelectionBar.AddAction("worlds.quickPlay", () => WithSelectedWorld(row => _ = EnterWorldAsync(row)));
        SelectionBar.AddAction("worlds.rename", () => WithSelectedWorld(row => _ = RenameAsync(row)));
        SelectionBar.AddAction("community.dataPacks", () => WithSelectedWorld(row => DataPacksRequested?.Invoke(row.World)));
        SelectionBar.AddAction("worlds.copySeed", () => WithSelectedWorld(CopySeed), enabled: () => WorldList.SelectedItem is WorldRow { World.Seed: not null });
        SelectionBar.AddAction("worlds.backup", () => WithSelectedWorld(row => _ = BackupAsync(row)));
        SelectionBar.AddAction("worlds.restore", () => WithSelectedWorld(row => _ = ShowBackupsAsync(row)));
        SelectionBar.AddAction("worlds.resetIcon", () => WithSelectedWorld(row => _ = ResetIconAsync(row)));
    }

    private void WithSelectedWorld(Action<WorldRow> action)
    {
        if (WorldList.SelectedItem is WorldRow row) action(row);
    }

    private void WorldList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectionBar is null) return;
        var selected = WorldList.SelectedItem as WorldRow;
        if (_backupWorld is not null && !ReferenceEquals(_backupWorld, selected)) CloseBackups();
        if (DetailsReveal.IsExpanded && !ReferenceEquals(DetailsReveal.DataContext, selected)) DetailsReveal.IsExpanded = false;
        SelectionBar.UpdateSelection(selected is null ? 0 : 1, selected is null ? "" : string.Format(L("selected"), selected.Name));
    }

    internal void SetVersion(LocalMinecraftVersion version)
    {
        _revision++;
        _scan?.Cancel();
        _version = version;
        WorldList.ItemsSource = null;
        CloseBackups();
        DetailsReveal.IsExpanded = false;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDropTarget();
        await RefreshAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _windowsDrop?.Dispose();
        _windowsDrop = null;
        _scan?.Cancel();
        CloseBackups();
        DetailsReveal.IsExpanded = false;
    }

    private void DropTarget_Changed(object sender, DependencyPropertyChangedEventArgs e) => UpdateDropTarget();

    private void UpdateDropTarget()
    {
        if (!IsLoaded || !IsVisible || !IsHitTestVisible)
        {
            _windowsDrop?.Dispose();
            _windowsDrop = null;
            return;
        }

        _windowsDrop ??= WindowsFileDropTarget.Attach(this, AcceptWorldZips, FilesDropped, DropFailed, MainWindow.IsVersionFileDropAllowed);
    }

    internal Task RefreshAsync() => RefreshAsync(null);

    private async Task RefreshAsync(string? selectFolder)
    {
        if (_version is not { } version) return;
        _scan?.Cancel();
        using CancellationTokenSource request = new();
        _scan = request;
        var token = request.Token;
        string saves = Saves(version);
        Status("reading");
        App.Logger.Information("Worlds", $"开始扫描世界: {saves}.");
        try
        {
            var worlds = await LocalMinecraftWorldManager.ScanAsync(saves, token);
            var rows = await Task.Run(() => worlds.Select(world => new WorldRow(world, ModIconDecoder.Decode(world.Icon))).ToArray(), token);
            token.ThrowIfCancellationRequested();
            string? previous = selectFolder ?? (WorldList.SelectedItem as WorldRow)?.FolderName;
            WorldList.ItemsSource = rows;
            WorldList.SelectedItem = rows.FirstOrDefault(row => string.Equals(previous, row.FolderName, StringComparison.OrdinalIgnoreCase));
            if (selectFolder is not null && WorldList.SelectedItem is { } selected) WorldList.ScrollIntoView(selected);
            if (rows.Length == 0) Status("empty");
            else ListStatus.Visibility = Visibility.Collapsed;
            foreach (var world in worlds.Where(world => world.MetadataError is not null))
                App.Logger.Warning("Worlds", $"无法读取世界元数据: {world.DirectoryPath}. {world.MetadataError}");
            App.Logger.Information("Worlds", $"世界扫描完成: {saves}, 数量={rows.Length}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsFileError(error))
        {
            if (token.IsCancellationRequested) return;
            App.Logger.Error("Worlds", $"世界扫描失败: {saves}.", error);
            Status("scanFailed");
        }
        finally
        {
            if (ReferenceEquals(_scan, request)) _scan = null;
        }
    }

    private void Status(string key)
    {
        ListStatus.SetResourceReference(TextBlock.TextProperty, "I18n.worlds." + key);
        ListStatus.Visibility = Visibility.Visible;
    }

    private void DataPacks_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) DataPacksRequested?.Invoke(row.World);
    }

    private async void WorldList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin || ItemsControl.ContainerFromElement(WorldList, origin) is not ListBoxItem { DataContext: WorldRow row }) return;
        for (DependencyObject? element = origin; element is Visual; element = VisualTreeHelper.GetParent(element))
        {
            if (element is Button) return;
            if (element is ListBoxItem) break;
        }

        e.Handled = true;
        await EnterWorldAsync(row);
    }

    private async void QuickPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) await EnterWorldAsync(row);
    }

    private Task EnterWorldAsync(WorldRow row) => QuickPlayRequested?.Invoke(row.World) ?? Task.CompletedTask;

    private void LocalInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) OpenLocalInfo(row);
    }

    private void OpenLocalInfo(WorldRow row)
    {
        WorldList.SelectedItem = row;
        CloseBackups();
        DetailsReveal.DataContext = row;
        DetailsReveal.IsExpanded = true;
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e) => DetailsReveal.IsExpanded = false;

    private static void CopySeed(WorldRow row)
    {
        if (row.World.Seed is not { } seed) return;
        try
        {
            Clipboard.SetText(seed.ToString(CultureInfo.InvariantCulture));
            App.Logger.Information("Worlds", "已复制世界种子.");
            LauncherToasts.Success(L("seedCopied"));
        }
        catch (ExternalException error)
        {
            App.Logger.Error("Worlds", "复制世界种子失败.", error);
            LauncherToasts.Error(L("copySeedFailed"));
        }
    }

    private void OpenSaves_Click(object sender, RoutedEventArgs e)
    {
        if (_version is { } version) OpenFolder(Saves(version));
    }

    private async void ImportWorld_Click(object sender, RoutedEventArgs e)
    {
        if (_version is not { } version) return;
        int? choice = await LauncherDialogs.ChooseAsync(L("import"), L("importSourcePrompt"), L("importFolder"), L("importZip"));
        if (Window.GetWindow(this) is not { } owner) return;
        string[] sources = choice switch
        {
            0 when SelectWorldFolder(owner) is { } folder => [folder],
            1 => SelectWorldZips(owner),
            _ => []
        };
        if (sources.Length > 0) await ImportWorldsAsync(version, sources, zipOnly: choice == 1);
    }

    private static bool AcceptWorldZips(string[] files) => files.Length > 0
        && files.All(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase));

    private void FilesDropped(string[] files)
    {
        if (_version is not { } version) return;
        if (AcceptWorldZips(files)) _ = ImportWorldsAsync(version, files, zipOnly: true);
        else LauncherToasts.Error(L("importZipOnly"));
    }

    private static void DropFailed(Exception error)
    {
        App.Logger.Error("Worlds", "无法接收世界 ZIP 拖放.", error);
        LauncherToasts.Error(L("dropFailed"));
    }

    private async Task ImportWorldsAsync(LocalMinecraftVersion version, string[] sources, bool zipOnly)
    {
        string? importedFolder = null;
        string subject = sources.Length == 1 ? Path.GetFileName(sources[0]) : string.Format(L("importZipCount"), sources.Length);
        await RunTaskAsync(version, subject, "import", async context =>
        {
            var token = context.CancellationToken;
            int failed = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                string source = sources[index];
                App.Logger.Information("Worlds", $"开始导入世界: {source}, 版本目录={version.FolderPath}.");
                try
                {
                    if (zipOnly && (!string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase) || Directory.Exists(source)))
                        throw new InvalidDataException(L("importZipOnly"));

                    string importedDirectory = await LocalMinecraftWorldManager.ImportAsync(Saves(version), source, token);
                    importedFolder = Path.GetFileName(importedDirectory);
                    App.Logger.Information("Worlds", $"世界已导入: {importedDirectory}.");
                }
                catch (Exception error) when (sources.Length > 1 && IsFileError(error))
                {
                    failed++;
                    App.Logger.Error("Worlds", $"无法导入世界: {source}.", error);
                }

                context.ReportProgress((index + 1) * 100d / sources.Length);
            }

            if (failed > 0) throw new IOException(string.Format(L("importBatchFailed"), sources.Length - failed, failed));
        }, () => importedFolder);
    }

    private static string? SelectWorldFolder(Window owner)
    {
        var picker = new OpenFolderDialog { Title = L("importFolder"), Multiselect = false };
        return picker.ShowDialog(owner) == true ? picker.FolderName : null;
    }

    private static string[] SelectWorldZips(Window owner)
    {
        var picker = new OpenFileDialog { Title = L("importZip"), Filter = L("importZipFilter"), DefaultExt = ".zip", CheckFileExists = true, Multiselect = true };
        return picker.ShowDialog(owner) == true ? picker.FileNames : [];
    }

    private async Task RenameAsync(WorldRow row)
    {
        if (_version is not { } version) return;
        string? name = await LauncherDialogs.InputAsync(L("rename"), L("renamePrompt"), row.Name,
            value => value.Length is 0 or > 128 || value.Any(char.IsControl) ? L("invalidName") : null);
        if (name is null || name == row.Name) return;
        bool completed = await RunOperationAsync(version, row, "rename", async token =>
        {
            App.Logger.Information("Worlds", $"开始重命名世界: {row.World.DirectoryPath}.");
            await LocalMinecraftWorldManager.RenameAsync(row.World.DirectoryPath, name, token);
            App.Logger.Information("Worlds", $"世界已重命名: {row.World.DirectoryPath}.");
        });
        if (completed && IsLoaded && ReferenceEquals(version, _version)) await RefreshAsync(row.FolderName);
    }

    private void RevealWorld_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) RevealWorld(row);
    }

    private static void RevealWorld(WorldRow row) => OpenFolder(row.World.DirectoryPath, reveal: true);

    private static void OpenFolder(string path, bool reveal = false)
    {
        try
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            var startInfo = reveal ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") : new ProcessStartInfo(path);
            startInfo.UseShellExecute = true;
            using var process = Process.Start(startInfo);
            App.Logger.Information("Worlds", $"已打开存档位置: {path}.");
        }
        catch (Exception error) when (IsFileError(error) || error is Win32Exception)
        {
            App.Logger.Error("Worlds", $"无法打开世界目录: {path}.", error);
            LauncherToasts.Error(L("folderFailed"));
        }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) await BackupAsync(row);
    }

    private async Task BackupAsync(WorldRow row)
    {
        if (_version is not { } version) return;
        bool completed = await RunOperationAsync(version, row, "backup", async token =>
        {
            var backup = await LocalMinecraftWorldManager.BackupAsync(Saves(version), Backups(version), row.FolderName, token);
            App.Logger.Information("Worlds", $"世界备份已保存: {Path.Combine(Backups(version), row.FolderName, backup.FileName)}.");
        });
        if (completed && ReferenceEquals(version, _version) && ReferenceEquals(_backupWorld, row)) await ShowBackupsAsync(version, row);
    }

    private async void ShowBackups_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) await ShowBackupsAsync(row);
    }

    private Task ShowBackupsAsync(WorldRow row)
    {
        WorldList.SelectedItem = row;
        DetailsReveal.IsExpanded = false;
        return _version is { } version ? ShowBackupsAsync(version, row) : Task.CompletedTask;
    }

    private async Task ShowBackupsAsync(LocalMinecraftVersion version, WorldRow row)
    {
        int request = ++_backupRequest;
        int revision = _revision;
        try
        {
            var backups = await LocalMinecraftWorldManager.ListBackupsAsync(Backups(version), row.FolderName);
            if (request != _backupRequest || revision != _revision) return;
            if (backups.Count == 0)
            {
                CloseBackups();
                LauncherToasts.Information(L("noBackups"));
                return;
            }

            _backupWorld = row;
            BackupCard.Header = string.Format(L("backupList"), row.World.Name);
            BackupBox.ItemsSource = backups.Select(backup => new BackupChoice(backup)).ToArray();
            BackupBox.SelectedIndex = 0;
            BackupReveal.IsExpanded = true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            App.Logger.Error("Worlds", $"无法读取世界备份: {row.World.DirectoryPath}.", error);
            if (request == _backupRequest && revision == _revision) LauncherToasts.Error(L("backupListFailed"));
        }
    }

    private void CloseBackups_Click(object sender, RoutedEventArgs e) => CloseBackups();

    private void CloseBackups()
    {
        _backupRequest++;
        _backupWorld = null;
        BackupReveal.IsExpanded = false;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_version is not { } version || _backupWorld is not { } row || BackupBox.SelectedItem is not BackupChoice choice) return;
        if (!await LauncherDialogs.ConfirmAsync(L("restore"), string.Format(L("restoreConfirm"), row.World.Name, choice.Label))) return;
        bool completed = await RunOperationAsync(version, row, "restore", async token =>
        {
            var checkpoint = await LocalMinecraftWorldManager.BackupAsync(Saves(version), Backups(version), row.FolderName, token);
            App.Logger.Information("Worlds", $"还原前已备份当前世界: {row.World.DirectoryPath}, {checkpoint.FileName}.");
            await LocalMinecraftWorldManager.RestoreAsync(Saves(version), Backups(version), row.FolderName, choice.Backup.FileName, token);
        });
        if (completed && ReferenceEquals(version, _version))
        {
            CloseBackups();
            if (IsLoaded) await RefreshAsync();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) await DeleteAsync(row);
    }

    private async Task DeleteAsync(WorldRow row)
    {
        if (_version is not { } version) return;
        if (!await LauncherDialogs.ConfirmAsync(L("delete"), string.Format(L("deleteConfirm"), row.World.Name), confirmText: L("delete"))) return;
        bool completed = await RunOperationAsync(version, row, "delete", token => LocalMinecraftWorldManager.DeleteAsync(Saves(version), Backups(version), row.FolderName, token));
        if (completed && ReferenceEquals(version, _version))
        {
            if (ReferenceEquals(_backupWorld, row)) CloseBackups();
            if (IsLoaded) await RefreshAsync();
        }
    }

    private async void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldRow row }) await ResetIconAsync(row);
    }

    private async Task ResetIconAsync(WorldRow row)
    {
        if (_version is not { } version) return;
        if (!await LauncherDialogs.ConfirmAsync(L("resetIcon"), string.Format(L("resetIconConfirm"), row.Name),
                confirmText: LocalizationManager.Get("reset.button"))) return;
        bool completed = await RunOperationAsync(version, row, "resetIcon", async token =>
            await LocalMinecraftWorldManager.ResetIconAsync(row.World.DirectoryPath, token));
        if (completed && IsLoaded && ReferenceEquals(_version, version)) await RefreshAsync();
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_version is not { } version || _backupWorld is not { } row || BackupBox.SelectedItem is not BackupChoice choice) return;
        if (!await LauncherDialogs.ConfirmAsync(L("deleteBackup"), string.Format(L("deleteBackupConfirm"), row.Name, choice.Label),
                confirmText: LocalizationManager.Get("common.delete"))) return;
        bool completed = await RunOperationAsync(version, row, "deleteBackup",
            token => LocalMinecraftWorldManager.DeleteBackupAsync(Backups(version), row.FolderName, choice.Backup.FileName, token));
        if (completed && ReferenceEquals(version, _version) && ReferenceEquals(_backupWorld, row)) await ShowBackupsAsync(version, row);
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_version is not { } version || _backupWorld is not { } row || BackupBox.SelectedItem is not BackupChoice choice) return;
        var picker = new SaveFileDialog
        {
            Title = L("exportBackup"), Filter = L("backupZipFilter"), DefaultExt = ".zip", AddExtension = true, OverwritePrompt = false,
            FileName = row.FolderName + "-" + choice.Backup.FileName
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        bool overwrite = File.Exists(picker.FileName);
        if (overwrite && !await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("toolbox.overwriteExistingFile"),
                string.Format(LocalizationManager.Get("toolbox.confirmOverwrite"), Path.GetFileName(picker.FileName)),
                confirmText: LocalizationManager.Get("toolbox.overwrite"))) return;
        await RunOperationAsync(version, row, "exportBackup", token => LocalMinecraftWorldManager.ExportBackupAsync(
            Backups(version), row.FolderName, choice.Backup.FileName, picker.FileName, overwrite, token));
    }

    private Task<bool> RunOperationAsync(LocalMinecraftVersion version, WorldRow row, string operation, Func<CancellationToken, Task> execute) => RunOperationAsync(version, row.World.Name, operation, execute);

    private Task<bool> RunOperationAsync(LocalMinecraftVersion version, string subject, string operation, Func<CancellationToken, Task> execute) =>
        RunTaskAsync(version, subject, operation, context => execute(context.CancellationToken));

    private async Task<bool> RunTaskAsync(LocalMinecraftVersion version, string subject, string operation, Func<LauncherTaskContext, Task> execute, Func<string?>? refreshSelection = null)
    {
        if (!ReferenceEquals(_version, version)) return false;
        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, version.FolderPath, StringComparison.OrdinalIgnoreCase)))
        {
            LauncherToasts.Information(L("versionBusy"));
            return false;
        }

        string label = L(operation);
        string title = string.Format(L("taskTitle"), label, subject);
        LauncherToasts.Information(string.Format(L("started"), title));
        var task = App.Tasks.Start(title, [new LauncherTaskStage(label, execute)], version.FolderPath);
        await task.Completion;
        if ((task.Status != LauncherTaskStatus.Completed || refreshSelection is not null) && IsLoaded && ReferenceEquals(version, _version))
            await RefreshAsync(refreshSelection?.Invoke());
        return task.Status == LauncherTaskStatus.Completed;
    }

    private static string Saves(LocalMinecraftVersion version) => Path.Combine(version.FolderPath, "saves");
    private static string Backups(LocalMinecraftVersion version) => Path.Combine(version.FolderPath, "ELCL", "world-backups");
    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException;
    private static string Size(long size) => DataSizeFormatter.Format(size, App.Settings.MemoryUnit, "0.##", minimumPower: 0);
    private static string L(string key) => LocalizationManager.Get("worlds." + key);

    private sealed record BackupChoice(MinecraftWorldBackup Backup)
    {
        public string Label => $"{Backup.CreatedAt.ToLocalTime():G} · {Size(Backup.Size)}";
    }

    internal sealed record WorldRow(LocalMinecraftWorld World, ImageSource? Icon)
    {
        public string Name => World.Name;
        public string FolderName => Path.GetFileName(World.DirectoryPath);
        public bool HasIcon => Icon is not null;
        public Geometry FallbackIcon => World.MetadataError is null ? LauncherIcons.Globe : LauncherIcons.Info;

        public string Summary => string.Join(" · ", new[]
        {
            World.GameMode is { } mode ? L("mode." + mode.ToString().ToLowerInvariant()) : L("mode.unknown"),
            World.LastPlayed is { } time ? string.Format(CultureInfo.CurrentCulture, L("lastPlayed"), time.ToLocalTime()) : L("lastPlayedUnknown"),
            World.Size is { } size ? Size(size) : L("sizeUnknown"),
            World.GameVersion,
            World.MetadataError is not null ? L("metadataError") : World.UsedPreviousMetadata ? L("previousMetadata") : null
        }.Where(text => !string.IsNullOrWhiteSpace(text)));

        public string Details => string.Join(Environment.NewLine, new[]
        {
            Summary,
            string.Format(L("infoSeed"), World.Seed?.ToString(CultureInfo.InvariantCulture) ?? L("unknown")),
            string.Format(L("infoDifficulty"), World.Difficulty is { } difficulty ? L("difficulty." + difficulty.ToString().ToLowerInvariant()) : L("unknown")),
            string.Format(L("infoCommands"), L(World.AllowCommands switch { true => "allowed", false => "disallowed", _ => "unknown" })),
            string.Format(L("infoDataVersion"), World.DataVersion?.ToString(CultureInfo.InvariantCulture) ?? L("unknown")),
            string.Format(L("infoPath"), World.DirectoryPath)
        });
    }
}
