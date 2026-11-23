using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.SystemTools;
using ErhioLab.Craft.Launcher.Tasks;
using ErhioLab.Craft.Launcher.Versions;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Launch;

public partial class VersionPacksPage
{
    private readonly MinecraftPackKind _kind;
    private LocalMinecraftVersion? _version;
    private string _gameDirectory = "";
    private string _directory = "";
    private CancellationTokenSource? _scan;
    private Task _scanWork = Task.CompletedTask;
    private CancellationTokenSource? _lookup;
    private IReadOnlyList<LocalMinecraftPackRow> _rows = [];
    private WindowsFileDropTarget? _windowsDrop;
    private bool _busy;
    private int _generation;

    internal event Action<ModProjectKind, ModBrowserContext>? BrowseRequested;
    internal event Action<ModProjectRow, ModBrowserContext>? DetailsRequested;
    internal event Action? BackRequested;

    private bool IsDataPack => _kind == MinecraftPackKind.DataPack;
    private ModProjectKind ProjectKind => _kind switch
    {
        MinecraftPackKind.ResourcePack => ModProjectKind.ResourcePack,
        MinecraftPackKind.ShaderPack => ModProjectKind.ShaderPack,
        _ => ModProjectKind.DataPack
    };
    private ModBrowserContext BrowserContext(LocalMinecraftVersion version) => new(version.GameVersion, null, version.FolderPath)
    {
        WorldDataPacksDirectory = IsDataPack ? _directory : null
    };

    public VersionPacksPage() : this(MinecraftPackKind.ResourcePack)
    {
    }

    internal VersionPacksPage(MinecraftPackKind kind)
    {
        _kind = kind;
        InitializeComponent();
        HeaderCard.SetResourceReference(HeaderedContentControl.HeaderProperty,
            kind == MinecraftPackKind.ResourcePack ? "I18n.community.resourcePacks" : "I18n.community.shaderPacks");
        BrowseButton.SetResourceReference(ContentControl.ContentProperty, "I18n.localPacks." + (kind switch
        {
            MinecraftPackKind.ResourcePack => "downloadResourcePacks",
            MinecraftPackKind.ShaderPack => "downloadShaderPacks",
            _ => "downloadDataPacks"
        }));
        PageHint.SetResourceReference(TextBlock.TextProperty, "I18n.localPacks." + (kind switch
        {
            MinecraftPackKind.ResourcePack => "resourcePacksHint",
            MinecraftPackKind.ShaderPack => "shaderPacksHint",
            _ => "dataPacksHint"
        }));
        if (IsDataPack)
        {
            WorldBackButton.Visibility = Visibility.Visible;
        }
        SelectionBar.EnableRequested += () => _ = ChangeAsync(SelectedRows(), true);
        SelectionBar.DisableRequested += () => _ = ChangeAsync(SelectedRows(), false);
        SelectionBar.DeleteRequested += () => _ = DeleteAsync(SelectedRows());
        SelectionBar.DeselectRequested += PackList.UnselectAll;
        SelectionBar.LocalInfoRequested += () =>
        {
            if (PackList.SelectedItem is LocalMinecraftPackRow row) OpenLocalInfo(row);
        };
        if (_kind == MinecraftPackKind.ShaderPack)
        {
            SelectionBar.AddAction("localPacks.importShaderConfig", () => TransferSelectedShaderConfig(importing: true), count => count == 1);
            SelectionBar.AddAction("localPacks.exportShaderConfig", () => TransferSelectedShaderConfig(importing: false), count => count == 1,
                () => PackList.SelectedItem is LocalMinecraftPackRow { HasShaderConfig: true });
        }
    }

    private void TransferSelectedShaderConfig(bool importing)
    {
        if (PackList.SelectedItems.Count == 1 && PackList.SelectedItem is LocalMinecraftPackRow row)
            _ = TransferShaderConfigAsync(row, importing);
    }

    internal void SetContext(string gameDirectory, LocalMinecraftVersion version) => SetDirectory(gameDirectory, version,
        Path.Combine(version.FolderPath, _kind == MinecraftPackKind.ResourcePack ? "resourcepacks" : "shaderpacks"));

    internal void SetWorldContext(string gameDirectory, LocalMinecraftVersion version, LocalMinecraftWorld world)
    {
        HeaderCard.Header = string.Format(LocalizationManager.Get("worlds.dataPacksTitle"), world.Name);
        SetDirectory(gameDirectory, version, Path.Combine(world.DirectoryPath, "datapacks"));
    }

    private void WorldBack_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void SetDirectory(string gameDirectory, LocalMinecraftVersion version, string directory)
    {
        _generation++;
        _scan?.Cancel();
        _lookup?.Cancel();
        _version = version;
        _gameDirectory = gameDirectory;
        _directory = directory;
        DetailsReveal.IsExpanded = false;
        DetailsReveal.DataContext = null;
        _rows = [];
        PackList.ItemsSource = null;
        SearchBox.Clear();
        if (IsLoaded) _ = RefreshAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        CommunityMods.Installed += Installed;
        UpdateDropTarget();
        await RefreshAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CommunityMods.Installed -= Installed;
        _scan?.Cancel();
        _lookup?.Cancel();
        DetailsReveal.IsExpanded = false;
        _windowsDrop?.Dispose();
        _windowsDrop = null;
    }

    private void DropTarget_Changed(object sender, DependencyPropertyChangedEventArgs e) => UpdateDropTarget();

    private void Installed(string directory)
    {
        if (!_busy && string.Equals(directory, _directory, StringComparison.OrdinalIgnoreCase)) _ = RefreshAsync();
    }

    private void UpdateDropTarget()
    {
        if (!IsLoaded || !IsVisible || !IsHitTestVisible)
        {
            _windowsDrop?.Dispose();
            _windowsDrop = null;
            return;
        }

        _windowsDrop ??= WindowsFileDropTarget.Attach(this, AcceptZip, FilesDropped, DropFailed, MainWindow.IsVersionFileDropAllowed);
        _windowsDrop?.SetEnabled(!_busy);
    }

    internal Task RefreshAsync() => _scanWork = RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        if (_version is not { } version || _busy) return;
        _scan?.Cancel();
        using var request = new CancellationTokenSource();
        _scan = request;
        var token = request.Token;
        string directory = _directory;
        string gameDirectory = _gameDirectory;
        string? previousDetails = DetailsReveal.IsExpanded ? (DetailsReveal.DataContext as LocalMinecraftPackRow)?.Path : null;
        ShowStatus("reading");
        App.Logger.Information("Packs", $"扫描本地包: 类型={_kind}, 目录={directory}.");
        try
        {
            var rows = await Task.Run(async () =>
            {
                LocalPackFiles.ValidateDirectory(directory);
                var paths = LocalMinecraftPackScanner.Enumerate(directory, token);
                LocalPackPreferences? settings = null;
                MinecraftWorldDataPackSelection? dataPacks = null;
                try
                {
                    if (IsDataPack)
                    {
                        if (paths.Count > 0) dataPacks = await LocalMinecraftWorldManager.ReadDataPackSelectionAsync(Path.GetDirectoryName(directory)!, token);
                    }
                    else settings = LocalPackPreferences.Load(version.FolderPath, _kind, version.GameVersion);
                }
                catch (Exception error) when (IsPackFileError(error))
                {
                    App.Logger.Error("Packs", $"无法读取包的启用状态: {directory}.", error);
                }

                return paths.Select(path =>
                {
                    string fileName = Path.GetFileName(path);
                    string id = "file/" + fileName;
                    bool? enabled = dataPacks is null ? settings?.IsEnabled(fileName)
                        : dataPacks.Enabled.Contains(id, StringComparer.Ordinal) ? true
                        : dataPacks.Disabled.Contains(id, StringComparer.Ordinal) ? false : null;
                    var row = new LocalMinecraftPackRow(path, _kind, enabled, canChangeSelection: dataPacks is not null);
                    if (row.IsShaderPack) row.SetHasShaderConfig(File.Exists(LocalPackFiles.ShaderConfigName(path)));
                    return row;
                }).ToArray();
            }, token);
            token.ThrowIfCancellationRequested();
            _rows = rows;
            PackList.ItemsSource = _rows;
            ApplyFilter();
            if (previousDetails is not null && _rows.FirstOrDefault(row => row.Path == previousDetails) is { } details)
                OpenLocalInfo(details);
            else DetailsReveal.IsExpanded = false;

            Task<MinecraftPackFormat?> currentFormat = _kind == MinecraftPackKind.ResourcePack && _rows.Count > 0
                ? ReadClientFormatAsync(gameDirectory, version, token)
                : Task.FromResult<MinecraftPackFormat?>(null);
            foreach (var row in _rows)
            {
                var pack = await LocalMinecraftPackScanner.ReadAsync(row.Path, _kind, token);
                var icon = pack.Icon is { } bytes ? await Task.Run(() => ModIconDecoder.Decode(bytes), token) : null;
                token.ThrowIfCancellationRequested();
                row.Apply(pack, icon);
                if (pack.Error is { } error) App.Logger.Warning("Packs", $"无法读取包: {pack.Path}. {error}");
            }

            var format = await currentFormat;
            token.ThrowIfCancellationRequested();
            if (format is not null)
                foreach (var row in _rows)
                    row.SetClientFormat(format);
            ApplyFilter();
            App.Logger.Information("Packs", $"本地包扫描完成: 类型={_kind}, 数量={_rows.Count}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            if (!ReferenceEquals(_scan, request)) return;
            App.Logger.Error("Packs", $"扫描本地包失败: {directory}.", error);
            ShowStatus("scanFailed");
        }
        finally
        {
            if (ReferenceEquals(_scan, request)) _scan = null;
        }
    }

    private static async Task<MinecraftPackFormat?> ReadClientFormatAsync(string gameDirectory, LocalMinecraftVersion version, CancellationToken token)
    {
        if (!version.IsValid) return null;
        try
        {
            var metadata = await MinecraftLaunchVersion.LoadAsync(gameDirectory, version.JsonPath, cancellationToken: token);
            return await LocalMinecraftPackScanner.ReadClientResourceFormatAsync(metadata.ClientJarPath, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            App.Logger.Information("Packs", $"无法读取客户端资源包格式: {version.Id}. {error.Message}");
            return null;
        }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (PackList is not null) ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (PackList.ItemsSource is null) return;
        string query = SearchBox.Text.Trim();
        CollectionViewSource.GetDefaultView(PackList.ItemsSource).Filter = query.Length > 0
            ? item => item is LocalMinecraftPackRow row && row.Matches(query)
            : null;
        if (PackList.Items.Count == 0) ShowStatus(_rows.Count == 0 ? IsDataPack ? "noDataPacks" : "empty" : "noResults");
        else EmptyText.Visibility = Visibility.Collapsed;
    }

    private void ShowStatus(string key)
    {
        EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.localPacks." + key);
        EmptyText.Visibility = Visibility.Visible;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_version is { } version) BrowseRequested?.Invoke(ProjectKind, BrowserContext(version));
    }

    private LocalMinecraftPackRow[] SelectedRows() => PackList.SelectedItems.Cast<LocalMinecraftPackRow>().ToArray();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => PackList.SelectAll();

    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        int count = PackList.SelectedItems.Count;
        SelectionBar?.UpdateSelection(count, string.Format(L("selected"), count),
            !IsDataPack || SelectedRows().All(row => row.CanToggle));
        if (DetailsReveal is not null && (count != 1 || !ReferenceEquals(DetailsReveal.DataContext, PackList.SelectedItem)))
            DetailsReveal.IsExpanded = false;
    }

    private async void Details_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalMinecraftPackRow row }) await OpenOnlineDetailsAsync(row);
    }

    private async void Pack_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source
                                                || ItemsControl.ContainerFromElement(PackList, source) is not ListBoxItem { DataContext: LocalMinecraftPackRow row } item) return;
        for (var child = source; child is Visual && !ReferenceEquals(child, item); child = VisualTreeHelper.GetParent(child))
            if (child is Button)
                return;
        e.Handled = true;
        await OpenOnlineDetailsAsync(row);
    }

    private async void Pack_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is Button || PackList.SelectedItem is not LocalMinecraftPackRow row) return;
        e.Handled = true;
        await OpenOnlineDetailsAsync(row);
    }

    private async Task OpenOnlineDetailsAsync(LocalMinecraftPackRow row)
    {
        if (_busy || _lookup is not null || _version is not { } version) return;
        using var request = new CancellationTokenSource();
        _lookup = request;
        App.Logger.Information("Packs", $"查询包的来源: {row.Path}.");
        LauncherToasts.Information(L("identifying"));
        try
        {
            LocalPackFiles.ValidatePack(_directory, row.Path);
            var project = Directory.Exists(row.Path) ? null : await CommunityMods.Client().IdentifyFileAsync(row.Path, ProjectKind, request.Token);
            if (!CanPresentLookup(request.Token)) return;
            if (project is null)
            {
                App.Logger.Information("Packs", $"未找到匹配的包来源: {row.Path}.");
                OpenLocalFallback(row, "sourceNotFound");
                return;
            }

            App.Logger.Information("Packs", $"找到包的来源: {row.Path}, 来源={project.Platform}/{project.Id}.");
            DetailsRequested?.Invoke(new ModProjectRow(project), BrowserContext(version));
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsPackFileError(error) || error is HttpRequestException or InvalidOperationException
                                          or KeyNotFoundException or FormatException or OperationCanceledException)
        {
            App.Logger.Error("Packs", $"包来源查询失败: {row.Path}.", error);
            if (CanPresentLookup(request.Token)) OpenLocalFallback(row, "identifyFailed");
        }
        finally
        {
            _lookup = null;
        }
    }

    private bool CanPresentLookup(CancellationToken token) => !token.IsCancellationRequested && !_busy && IsLoaded && IsVisible && IsHitTestVisible;

    private void OpenLocalFallback(LocalMinecraftPackRow requested, string message)
    {
        // 查询期间可能刷新过列表, 用原路径找到当前列表中的条目.
        var current = PackList.Items.OfType<LocalMinecraftPackRow>().FirstOrDefault(row =>
            string.Equals(row.Path, requested.Path, StringComparison.OrdinalIgnoreCase));
        if (current is null) return;
        OpenLocalInfo(current);
        LauncherToasts.Error(L(message));
    }

    private void OpenLocalInfo(LocalMinecraftPackRow row)
    {
        PackList.SelectedItem = row;
        DetailsReveal.DataContext = row;
        DetailsReveal.IsExpanded = true;
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e) => DetailsReveal.IsExpanded = false;

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalMinecraftPackRow { CanToggle: true } row }) await ChangeAsync([row], row.Enabled != true);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalMinecraftPackRow row }) await DeleteAsync([row]);
    }

    private async void ImportShaderConfig_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalMinecraftPackRow row }) await TransferShaderConfigAsync(row, importing: true);
    }

    private async void ExportShaderConfig_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalMinecraftPackRow row }) await TransferShaderConfigAsync(row, importing: false);
    }

    private async Task TransferShaderConfigAsync(LocalMinecraftPackRow row, bool importing)
    {
        if (_busy || !row.IsShaderPack || _kind != MinecraftPackKind.ShaderPack || _version is not { } version) return;
        int generation = _generation;
        string directory = _directory;
        string title = L(importing ? "importShaderConfig" : "exportShaderConfig");
        bool started = false;
        try
        {
            string config = LocalPackFiles.GetShaderConfigPath(directory, row.Path);
            if (importing) EnsureGameStopped(version);
            else if (!File.Exists(config)) throw new FileNotFoundException(L("shaderConfigMissing"), config);
            FileDialog picker = importing
                ? new OpenFileDialog { CheckFileExists = true }
                : new SaveFileDialog { FileName = Path.GetFileName(config), OverwritePrompt = false };
            picker.Title = title;
            picker.Filter = L("shaderConfigFilter");
            picker.DefaultExt = ".txt";
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            string external = Path.GetFullPath(picker.FileName);
            if (!Path.GetExtension(external).Equals(".txt", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L("shaderConfigTxtOnly"));
            if (string.Equals(external, config, StringComparison.OrdinalIgnoreCase)) return;
            string destination = importing ? config : external;
            bool overwrite = File.Exists(destination);
            if (overwrite && !await LauncherDialogs.ConfirmAsync(title, string.Format(L("shaderConfigOverwrite"), Path.GetFileName(destination)))) return;
            if (_busy || generation != _generation || !IsLoaded) return;
            if (importing) EnsureGameStopped(version);

            SetBusy(true);
            started = true;
            _lookup?.Cancel();
            App.Logger.Information("Packs", $"开始{(importing ? "导入" : "导出")}光影包配置: 光影包={row.Path}, 文件={external}.");
            await Task.Run(() => LocalPackFiles.TransferShaderConfig(directory, row.Path, external, importing, overwrite));
            App.Logger.Information("Packs", $"光影包配置{(importing ? "导入" : "导出")}完成: {destination}.");
            LauncherToasts.Success(L(importing ? "shaderConfigImported" : "shaderConfigExported"));
        }
        catch (Exception error) when (IsPackFileError(error))
        {
            App.Logger.Error("Packs", $"光影包配置{(importing ? "导入" : "导出")}失败: {row.Path}.", error);
            LauncherToasts.Error(string.Format(L("operationFailed"), error.Message));
        }
        finally
        {
            if (started) SetBusy(false);
            if (generation == _generation)
            {
                bool exists = File.Exists(LocalPackFiles.ShaderConfigName(row.Path));
                foreach (var current in _rows.Where(item => string.Equals(item.Path, row.Path, StringComparison.OrdinalIgnoreCase)))
                    current.SetHasShaderConfig(exists);
            }
        }
    }

    private async Task ChangeAsync(LocalMinecraftPackRow[] rows, bool enabled)
    {
        if (rows.Length == 0 || _busy) return;
        int generation = _generation;
        if (IsDataPack && !enabled && rows.Any(row => row.Enabled != false)
            && !await LauncherDialogs.ConfirmAsync(CommunityMods.L("disable"), L("disableDataPackConfirm"), confirmText: CommunityMods.L("disable"))) return;
        if (generation == _generation) await EditPacksAsync(rows, enabled, deleting: false);
    }

    private async Task DeleteAsync(LocalMinecraftPackRow[] rows)
    {
        int generation = _generation;
        string confirmation = _kind switch
        {
            MinecraftPackKind.ShaderPack => "deleteShaderConfirm",
            MinecraftPackKind.DataPack => "deleteDataPackConfirm",
            _ => "deleteConfirm"
        };
        if (_busy || rows.Length == 0 || !await LauncherDialogs.ConfirmAsync(CommunityMods.L("delete"), string.Format(L(confirmation), rows.Length))) return;
        if (generation == _generation) await EditPacksAsync(rows, enabled: false, deleting: true);
    }

    private async Task EditPacksAsync(LocalMinecraftPackRow[] rows, bool enabled, bool deleting)
    {
        if (_busy || rows.Length == 0 || _version is not { } version) return;
        string action = deleting ? "delete" : enabled ? "enable" : "disable";
        string directory = _directory;
        int generation = _generation;
        SetBusy(true);
        _lookup?.Cancel();
        App.Logger.Information("Packs", $"开始修改本地包: 操作={action}, 数量={rows.Length}, 目录={directory}.");
        LauncherToasts.Information(L("operating"));
        try
        {
            _scan?.Cancel();
            await _scanWork;
            if (generation != _generation) return;
            EnsureGameStopped(version);
            if (IsDataPack)
            {
                await EditDataPacksAsync(version, directory, rows, enabled, deleting);
                return;
            }
            bool deleteShaderConfig = deleting && _kind == MinecraftPackKind.ShaderPack;
            var preferences = await Task.Run(() =>
            {
                foreach (var row in rows)
                {
                    if (deleteShaderConfig) _ = LocalPackFiles.GetShaderConfigPath(directory, row.Path);
                    else LocalPackFiles.ValidatePack(directory, row.Path);
                }

                return LocalPackPreferences.Load(version.FolderPath, _kind, version.GameVersion);
            });
            if (enabled)
            {
                if (_kind == MinecraftPackKind.ShaderPack && rows.Length != 1) throw new IOException(L("oneShader"));
                var format = _kind == MinecraftPackKind.ResourcePack
                    ? await ReadClientFormatAsync(_gameDirectory, version, CancellationToken.None)
                    : null;
                foreach (var row in rows.Where(row => preferences.IsEnabled(row.FileName) != true))
                {
                    var pack = await LocalMinecraftPackScanner.ReadAsync(row.Path, _kind);
                    if (pack.Error is not null) throw new InvalidDataException(string.Format(L("invalidForEnable"), row.FileName));
                    if (format is { } currentFormat && !LocalMinecraftPackRow.IsInRange(pack, currentFormat))
                        throw new InvalidDataException(string.Format(L("incompatibleForEnable"), row.FileName));
                }
            }

            if (generation != _generation) return;
            EnsureGameStopped(version);
            preferences.SetEnabled(rows.Select(row => row.FileName).ToArray(), enabled, deleting);
            if (deleting)
                await Task.Run(() =>
                {
                    foreach (var row in rows) LocalPackFiles.Delete(directory, row.Path, deleteShaderConfig);
                });
            LauncherToasts.Success(L(deleting ? "deleted" : "settingsSaved"));
            App.Logger.Information("Packs", $"本地包操作完成: {action}, 数量={rows.Length}.");
        }
        catch (Exception error) when (IsPackFileError(error))
        {
            App.Logger.Error("Packs", $"本地包操作失败: {action}, 目录={directory}.", error);
            LauncherToasts.Error(string.Format(L("operationFailed"), error.Message));
        }
        finally
        {
            SetBusy(false);
            if (IsLoaded && generation == _generation) await RefreshAsync();
        }
    }

    private async Task EditDataPacksAsync(LocalMinecraftVersion version, string directory, LocalMinecraftPackRow[] rows, bool enabled, bool deleting)
    {
        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, version.FolderPath, StringComparison.OrdinalIgnoreCase)))
            throw new IOException(LocalizationManager.Get("worlds.versionBusy"));
        string action = CommunityMods.L(deleting ? "delete" : enabled ? "enable" : "disable");
        string title = string.Format(L("dataPackTask"), action, rows.Length);
        var stage = new LauncherTaskStage(title, async context =>
        {
            var token = context.CancellationToken;
            await Task.Run(() =>
            {
                foreach (var row in rows) LocalPackFiles.ValidatePack(directory, row.Path);
            }, token);
            if (enabled)
            {
                foreach (var row in rows)
                {
                    var pack = await LocalMinecraftPackScanner.ReadAsync(row.Path, _kind, token);
                    if (pack.Error is not null) throw new InvalidDataException(string.Format(L("invalidForEnable"), row.FileName));
                }
            }
            try
            {
                await LocalMinecraftWorldManager.SetDataPackSelectionAsync(Path.GetDirectoryName(directory)!,
                    rows.Select(row => row.FileName), deleting ? null : enabled, token);
            }
            catch (Exception error) when (error is InvalidDataException or FileNotFoundException)
            {
                throw new IOException(L("dataPackSettingsMissing"), error);
            }
            if (deleting)
                await Task.Run(() =>
                {
                    // 清理同批文件与记录时, 不在提交记录后半途响应取消.
                    foreach (var row in rows) LocalPackFiles.Delete(directory, row.Path, deleteShaderConfig: false);
                });
        });
        var task = App.Tasks.Start(title, [stage], version.FolderPath);
        await task.Completion;
    }

    private static void EnsureGameStopped(LocalMinecraftVersion version)
    {
        if (App.Tasks.Tasks.Any(task => task.IsActive && task.IsGameLaunch && string.Equals(task.TargetPath, version.FolderPath, StringComparison.OrdinalIgnoreCase)))
            throw new IOException(L("gameRunning"));
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ContentRoot.IsEnabled = !busy;
        _windowsDrop?.SetEnabled(!busy);
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (_version is not { } version) return;
        OpenLocation(() =>
        {
            if (!Directory.Exists(version.FolderPath)) throw new DirectoryNotFoundException();
            LocalPackFiles.ValidateDirectory(_directory);
            Directory.CreateDirectory(_directory);
            return new ProcessStartInfo(_directory) { UseShellExecute = true };
        });
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalMinecraftPackRow row })
            OpenLocation(() => new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true });
    }

    private static void OpenLocation(Func<ProcessStartInfo> info)
    {
        try
        {
            Process.Start(info());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            App.Logger.Error("Packs", "无法打开本地包所在位置.", error);
            LauncherToasts.Error(L("folderFailed"));
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _version is null) return;
        var picker = new OpenFileDialog { Filter = L("zipFilter"), Multiselect = true, CheckFileExists = true };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await ImportAsync(picker.FileNames);
    }

    private static bool AcceptZip(string[] files) => files.Length > 0
                                                     && files.All(path => !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase));

    private void FilesDropped(string[] files)
    {
        if (_busy || _version is null) return;
        if (AcceptZip(files)) _ = ImportAsync(files);
        else LauncherToasts.Error(L("zipOnly"));
    }

    private static void DropFailed(Exception error)
    {
        App.Logger.Error("Packs", "无法接收本地包拖放.", error);
        LauncherToasts.Error(L("dropFailed"));
    }

    private async Task ImportAsync(string[] files)
    {
        if (_busy || _version is null) return;
        SetBusy(true);
        int generation = _generation;
        string directory = _directory;
        App.Logger.Information("Packs", $"开始导入本地包: 类型={_kind}, 数量={files.Length}, 目录={directory}.");
        try
        {
            if (_version is not { } version || !Directory.Exists(version.FolderPath))
                throw new DirectoryNotFoundException(LocalizationManager.Get("versionSelector.directoryChanged"));
            if (IsDataPack) EnsureGameStopped(version);
            LocalPackFiles.ValidateDirectory(directory);
            int count = await LocalMinecraftPackScanner.ImportAsync(directory, files, _kind);
            LauncherToasts.Success(string.Format(L("imported"), count));
            App.Logger.Information("Packs", $"本地包导入完成: 类型={_kind}, 数量={count}, 目录={directory}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            App.Logger.Error("Packs", $"本地包导入失败: {directory}.", error);
            LauncherToasts.Error(string.Format(L("importFailed"), error.Message));
        }
        finally
        {
            SetBusy(false);
            if (IsLoaded && generation == _generation) await RefreshAsync();
        }
    }

    private static bool IsPackFileError(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException
        or ArgumentException or NotSupportedException or JsonException;

    private static string L(string key) => LocalizationManager.Get("localPacks." + key);
}
