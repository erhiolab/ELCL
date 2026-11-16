using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.SystemTools;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Launch;

public partial class VersionModsPage
{
    private string _directory = "";
    private ModBrowserContext _context = new(null, null, null);
    private CancellationTokenSource? _scan;
    private Task _scanWork = Task.CompletedTask;
    private IReadOnlyList<LocalModRow> _rows = [];
    private bool _busy;
    private int _generation;
    private WindowsFileDropTarget? _windowsDrop;
    internal event Action<ModBrowserContext>? BrowseRequested;
    internal event Action<ModProjectRow, ModBrowserContext>? DetailsRequested;

    public VersionModsPage() => InitializeComponent();

    internal void SetContext(ModBrowserContext context)
    {
        _context = context;
        _directory = Path.Combine(context.VersionDirectory!, "mods");
        _generation++;
        _scan?.Cancel();
        _rows = [];
        ModsList.ItemsSource = null;
        if (IsLoaded) _ = RefreshAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        CommunityMods.Installed += Installed;
        UpdateWindowsDropTarget();
        await RefreshAsync();
    }

    private void Page_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateWindowsDropTarget();

    private void UpdateWindowsDropTarget()
    {
        if (!IsLoaded || !IsVisible)
        {
            _windowsDrop?.Dispose();
            _windowsDrop = null;
            return;
        }

        _windowsDrop ??= WindowsFileDropTarget.TryAttach(this, WindowsFilesDropped, WindowsDropFailed);
        _windowsDrop?.SetEnabled(!_busy);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CommunityMods.Installed -= Installed;
        _scan?.Cancel();
        _windowsDrop?.Dispose();
        _windowsDrop = null;
    }

    private void Installed(string directory)
    {
        if (!_busy && string.Equals(directory, _directory, StringComparison.OrdinalIgnoreCase)) _ = RefreshAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();
    private Task RefreshAsync() => _scanWork = RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        if (string.IsNullOrEmpty(_directory)) return;
        _scan?.Cancel();
        var request = new CancellationTokenSource();
        _scan = request;
        EmptyText.Text = CommunityMods.L("loading");
        EmptyText.Visibility = Visibility.Visible;
        string directory = _directory;
        App.Logger.Information("Mods", $"扫描本地 Mods: {directory}.");
        try
        {
            var paths = await Task.Run(() => LocalModManager.EnumerateFiles(directory, request.Token), request.Token);
            if (!ReferenceEquals(_scan, request)) return;
            var rows = paths.Select(path => new LocalModRow(path)).ToArray();
            _rows = rows;
            ModsList.ItemsSource = rows;
            ApplyFilter();
            App.Logger.Information("Mods", $"本地 Mods 文件已列出: {paths.Count} 项, 开始后台读取详情.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background, request.Token);
            var pending = Channel.CreateUnbounded<LocalModRow>();
            var enrichment = Task.WhenAll(EnrichRowsAsync(pending.Reader, request), EnrichRowsAsync(pending.Reader, request));
            try
            {
                await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = request.Token }, async (row, token) =>
                {
                    LocalModInfo metadata;
                    ImageSource? icon = null;
                    try
                    {
                        metadata = await LocalModManager.ReadAsync(row.Mod.Path, token);
                        icon = CommunityMods.Decode(metadata.Icon);
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or NotSupportedException)
                    {
                        metadata = row.Mod with { Error = error.Message };
                        App.Logger.Error("Mods", $"读取 Mods 文件失败: {row.Mod.FileName}.", error);
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!ReferenceEquals(_scan, request)) return;
                        row.SetMetadata(metadata, icon);
                        if (SearchBox.Text.Trim().Length > 0) ApplyFilter();
                        pending.Writer.TryWrite(row);
                    }, DispatcherPriority.Background, token);
                });
                App.Logger.Information("Mods", $"本地 Mods 详情读取完成: {rows.Length} 项, 无法解析={rows.Count(row => row.Mod.Error is not null)}.");
            }
            finally
            {
                pending.Writer.TryComplete();
                await enrichment;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            App.Logger.Error("Mods", "扫描本地 Mods 失败.", error);
            EmptyText.Text = CommunityMods.L("scanFailed");
            LauncherToasts.Error(CommunityMods.L("scanFailed"));
        }
        finally
        {
            if (ReferenceEquals(_scan, request)) _scan = null;
            request.Dispose();
        }
    }

    private async Task EnrichRowsAsync(ChannelReader<LocalModRow> reader, CancellationTokenSource request)
    {
        await foreach (var row in reader.ReadAllAsync(request.Token))
        {
            request.Token.ThrowIfCancellationRequested();
            try
            {
                await row.EnrichAsync().WaitAsync(request.Token);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or TaskCanceledException)
            {
                App.Logger.Error("Mods", $"补充 Mods 来源信息失败: {row.Mod.FileName}.", error);
            }

            if (ReferenceEquals(_scan, request) && IsLoaded && SearchBox.Text.Trim().Length > 0) ApplyFilter();
        }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (ModsList is not null) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var view = CollectionViewSource.GetDefaultView(ModsList.ItemsSource);
        string query = SearchBox.Text.Trim();
        if (view is not null) view.Filter = item => item is LocalModRow row && (query.Length == 0 || row.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        EmptyText.Text = CommunityMods.L(_rows.Count == 0 ? "noLocalMods" : "noMatchingMods");
        EmptyText.Visibility = view is null || view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Browse_Click(object sender, RoutedEventArgs e) => BrowseRequested?.Invoke(_context);

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            CommunityMods.Open(_directory);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            App.Logger.Error("Mods", "打开 Mods 文件夹失败.", error);
            LauncherToasts.Error(CommunityMods.L("openFailed"));
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ModsList.SelectAll();
    private void Deselect_Click(object sender, RoutedEventArgs e) => ModsList.UnselectAll();

    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SelectionReveal is null) return;
        SelectionReveal.IsExpanded = ModsList.SelectedItems.Count > 0;
        SelectionText.Text = string.Format(CommunityMods.L("selected"), ModsList.SelectedItems.Count);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog picker = new() { Title = CommunityMods.L("installFromFile"), Filter = CommunityMods.L("fileFilter"), Multiselect = true };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await ImportAsync(picker.FileNames);
    }

    private static string[]? GetDroppedModFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop, false) || data.GetData(DataFormats.FileDrop, false) is not string[] { Length: > 0 } files) return null;
        return files.All(path =>
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            string name = path;
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".disable", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".litemod", StringComparison.OrdinalIgnoreCase);
        })
            ? files
            : null;
    }

    private void WindowsFilesDropped(string[] files)
    {
        if (_busy || string.IsNullOrEmpty(_directory))
        {
            App.Logger.Warning("Mods", $"忽略 Mods 拖入: 正在处理={_busy}, 已选择目录={!string.IsNullOrEmpty(_directory)}, 文件数={files.Length}.");
            return;
        }

        if (GetDroppedModFiles(new DataObject(DataFormats.FileDrop, files)) is not { } accepted)
        {
            App.Logger.Warning("Mods", $"拖入的文件不是支持的 Mods 文件: 文件数={files.Length}.");
            LauncherToasts.Error(CommunityMods.L("dropUnsupported"));
            return;
        }

        App.Logger.Information("Mods", $"开始导入拖入的 Mods: 文件数={accepted.Length}, 目录={_directory}.");
        _ = ImportAsync(accepted);
    }

    private void WindowsDropFailed(Exception error)
    {
        App.Logger.Error("Mods", "Mods 拖入失败.", error);
        LauncherToasts.Error(CommunityMods.L("dropFailed"));
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && !string.IsNullOrEmpty(_directory) && (e.AllowedEffects & DragDropEffects.Copy) != 0 && GetDroppedModFiles(e.Data) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_busy || string.IsNullOrEmpty(_directory) || (e.AllowedEffects & DragDropEffects.Copy) == 0 || GetDroppedModFiles(e.Data) is not { } files)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        await ImportAsync(files);
    }

    private Task ImportAsync(string[] files)
    {
        if (files.Length == 0) return Task.CompletedTask;
        int added = 0;
        return OperateAsync("import", async (directory, _) =>
        {
            var installed = await LocalModManager.InstallAsync(directory, files);
            added = installed.Count;
        }, () => string.Format(CommunityMods.L("importComplete"), added), notifyProgress: false);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalModRow row }) CommunityMods.Open(row.Mod.Path, reveal: true);
    }

    private async void Details_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalModRow row }) await DetailsAsync(row);
    }

    private async void Mod_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element && FindButton(element) is null && ModsList.SelectedItem is LocalModRow row) await DetailsAsync(row);
    }

    private static Button? FindButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button button) return button;
            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private async Task DetailsAsync(LocalModRow row)
    {
        if (_busy || row.IsParsing) return;
        _busy = true;
        _windowsDrop?.SetEnabled(false);
        int generation = _generation;
        var context = _context;
        LauncherToasts.Information(CommunityMods.L("identifying"));
        App.Logger.Information("Mods", $"查询本地 Mods 来源: {row.Mod.FileName}.");
        try
        {
            var project = row.Project ?? await CommunityMods.IdentifyAsync(row.Mod);
            if (generation != _generation || !IsLoaded) return;
            if (project is not null) DetailsRequested?.Invoke(new ModProjectRow(project), context);
            else await LauncherDialogs.WarningAsync(row.Title, string.Format(CommunityMods.L("localDetails"), row.Mod.Version ?? CommunityMods.L("unknown"), row.Mod.Description, row.Mod.FileName, row.Mod.Error ?? ""));
            App.Logger.Information("Mods", $"本地 Mods 详情已打开: {row.Mod.FileName}, 来源={project?.Platform}.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or TaskCanceledException)
        {
            App.Logger.Error("Mods", "识别本地 Mods 失败.", error);
            LauncherToasts.Error(CommunityMods.L("identifyFailed"));
        }
        finally
        {
            _busy = false;
            _windowsDrop?.SetEnabled(true);
        }
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalModRow row }) await ChangeAsync([row], !row.Mod.Enabled);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalModRow row }) await DeleteAsync([row]);
    }

    private async void EnableSelected_Click(object sender, RoutedEventArgs e) => await ChangeAsync(ModsList.SelectedItems.Cast<LocalModRow>().ToArray(), true);
    private async void DisableSelected_Click(object sender, RoutedEventArgs e) => await ChangeAsync(ModsList.SelectedItems.Cast<LocalModRow>().ToArray(), false);
    private async void DeleteSelected_Click(object sender, RoutedEventArgs e) => await DeleteAsync(ModsList.SelectedItems.Cast<LocalModRow>().ToArray());

    private Task ChangeAsync(LocalModRow[] rows, bool enabled) => OperateAsync(enabled ? "enable" : "disable", (directory, errors) =>
    {
        foreach (var row in rows)
            try
            {
                LocalModManager.SetEnabled(directory, row.Mod.Path, enabled);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add(error);
            }

        return Task.CompletedTask;
    });

    private async Task DeleteAsync(LocalModRow[] rows)
    {
        if (_busy || rows.Length == 0 || !await LauncherDialogs.ConfirmAsync(CommunityMods.L("delete"), string.Format(CommunityMods.L("deleteConfirm"), rows.Length))) return;
        await OperateAsync("delete", (directory, errors) =>
        {
            foreach (var row in rows)
                try
                {
                    LocalModManager.Delete(directory, row.Mod.Path);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                {
                    errors.Add(error);
                }

            return Task.CompletedTask;
        });
    }

    private async Task OperateAsync(string action, Func<string, List<Exception>, Task> operation,
        Func<string>? completionMessage = null, bool notifyProgress = true)
    {
        if (_busy || string.IsNullOrEmpty(_directory)) return;
        _busy = true;
        _windowsDrop?.SetEnabled(false);
        IsEnabled = false;
        string directory = _directory;
        int generation = _generation;
        App.Logger.Information("Mods", $"开始 Mods 操作: {action}, 目录={directory}.");
        if (notifyProgress) LauncherToasts.Information(CommunityMods.L("operating"));
        try
        {
            _scan?.Cancel();
            await _scanWork;
            if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, directory, StringComparison.OrdinalIgnoreCase))) throw new IOException(CommunityMods.L("installBusy"));
            List<Exception> errors = [];
            await Task.Run(() => operation(directory, errors));
            if (errors.Count > 0) throw new AggregateException(errors);
            string message = completionMessage?.Invoke() ?? CommunityMods.L("operationComplete");
            App.Logger.Information("Mods", $"Mods 操作完成: {action}. {message}");
            LauncherToasts.Success(message);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or AggregateException)
        {
            App.Logger.Error("Mods", $"Mods 操作失败: {action}.", error);
            LauncherToasts.Error(string.Format(CommunityMods.L("operationFailed"), error.Message));
        }
        finally
        {
            _busy = false;
            IsEnabled = true;
            _windowsDrop?.SetEnabled(true);
            if (generation == _generation && IsLoaded) await RefreshAsync();
        }
    }
}

public sealed class LocalModRow : INotifyPropertyChanged
{
    internal LocalModInfo Mod { get; private set; }
    public bool IsParsing { get; private set; }
    public bool CanViewDetails => !IsParsing;
    private string? _translated;
    internal ModProject? Project { get; private set; }
    private string DisplayName => (CommunityMods.IsTranslationEnabled ? _translated : null) ?? Project?.Name ?? Mod.Name;
    private bool FileNameFirst => App.Settings.ModListPresentation == ModListPresentation.FileNameFirst;
    private string OriginalName => Project?.Name ?? Mod.Name;
    private string Description => (string.IsNullOrWhiteSpace(Mod.Description) ? Project?.Summary ?? "" : Mod.Description).ReplaceLineEndings(" ");
    public string Title => FileNameFirst ? Mod.FileName : DisplayName;
    public string Name => Title;
    public string OriginalNameLabel => FileNameFirst || OriginalName == DisplayName ? "" : "  |  " + OriginalName;
    public string VersionLabel => string.IsNullOrWhiteSpace(Mod.Version) ? "" : "  |  " + Mod.Version;
    public string HeadingToolTip => string.Join(" | ", new[] { Title, FileNameFirst || OriginalName == DisplayName ? "" : OriginalName, Mod.Version }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string Subtitle => string.Join(" · ", new[] { FileNameFirst ? DisplayName : Mod.FileName, Description }.Where(value => value.Length > 0));
    public string Summary => Subtitle;

    public IReadOnlyList<string> CategoryLabels =>
        (IsParsing ? new[] { CommunityMods.L("readingDetails") } : Mod.Error is not null ? new[] { CommunityMods.L("malformedLabel") } : Array.Empty<string>())
        .Concat(Mod.Enabled ? [] : new[] { CommunityMods.L("disabled") })
        .Concat(Mod.Loaders.Select(ModPresentation.LoaderName))
        .Concat((Project?.Categories ?? []).Select(ModPresentation.CategoryName))
        .Distinct().Take(3).ToArray();

    public string ToolTip => Mod.Error is null ? string.Join(Environment.NewLine, new[] { HeadingToolTip, string.Join(" / ", CategoryLabels), Summary }) : string.Format(CommunityMods.L("malformed"), Mod.Error);
    public string SearchText => string.Join(" ", new[] { DisplayName, OriginalName, Mod.FileName, Mod.Version, Description, string.Join(" ", CategoryLabels) });
    public Geometry StatusIcon => Mod.Error is not null ? LauncherIcons.Error : Mod.Enabled ? LauncherIcons.ModsAndResources : LauncherIcons.Disable;
    public Geometry ToggleIcon => Mod.Enabled ? LauncherIcons.Disable : LauncherIcons.Check;
    public string ToggleLabel => CommunityMods.L(Mod.Enabled ? "disable" : "enable");
    public double IconOpacity => Mod.Enabled ? 1 : 0.4;
    public ImageSource? Icon { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal LocalModRow(LocalModInfo mod) => Mod = mod;

    internal LocalModRow(string path) : this(new LocalModInfo(path, Path.GetFileName(path), Path.GetFileName(path), null, "", [], [],
        !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".disable", StringComparison.OrdinalIgnoreCase), "", 0)) => IsParsing = true;

    internal void SetMetadata(LocalModInfo metadata, ImageSource? icon = null)
    {
        Mod = metadata;
        Icon = icon;
        IsParsing = false;
        NotifyNames();
        foreach (string property in new[] { nameof(Icon), nameof(IsParsing), nameof(CanViewDetails), nameof(StatusIcon), nameof(ToggleIcon), nameof(ToggleLabel), nameof(IconOpacity) }) PropertyChanged?.Invoke(this, new(property));
    }

    internal async Task EnrichAsync()
    {
        Icon ??= await Task.Run(() => CommunityMods.Decode(Mod.Icon));
        PropertyChanged?.Invoke(this, new(nameof(Icon)));
        if (Mod.Error is not null) return;
        if (Icon is null) Project = await CommunityMods.IdentifyAsync(Mod);
        if (Project is not null)
        {
            NotifyNames();
            await EnrichProjectAsync();
            return;
        }

        var translation = await CommunityMods.TranslationAsync(Mod.Name);
        _translated = translation is { ChineseName.Length: > 0 } ? translation.ChineseName : null;
        NotifyNames();
        Project = await CommunityMods.IdentifyAsync(Mod);
        if (Project is not null) await EnrichProjectAsync();
    }

    private async Task EnrichProjectAsync()
    {
        if (Project is not { } project) return;
        var translationTask = CommunityMods.TranslationAsync(project.Name, project);
        Icon ??= await CommunityMods.ImageAsync(project.Icon);
        PropertyChanged?.Invoke(this, new(nameof(Icon)));
        var translation = await translationTask;
        if (translation is { ChineseName.Length: > 0 }) _translated = translation.ChineseName;
        NotifyNames();
    }

    private void NotifyNames()
    {
        foreach (string property in new[] { nameof(Title), nameof(Name), nameof(OriginalNameLabel), nameof(VersionLabel), nameof(HeadingToolTip), nameof(Subtitle), nameof(Summary), nameof(CategoryLabels), nameof(ToolTip), nameof(SearchText) }) PropertyChanged?.Invoke(this, new(property));
    }
}
