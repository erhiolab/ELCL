using System.IO;
using System.ComponentModel;
using System.Windows.Media.Animation;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Tasks;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Download;

public partial class ModDetailsPage
{
    private readonly ModProjectRow _row;
    private readonly ModBrowserContext _context;
    private readonly List<ModFile> _files = [];
    private CancellationTokenSource? _request;
    private readonly HashSet<CancellationTokenSource> _dependencyRequests = [];
    private bool _ready;
    private bool _loaded;
    private bool _filesReady;
    private readonly Dictionary<string, IReadOnlyList<ModFile>> _fileCache = [];
    private string _lastVersion;
    private IReadOnlyList<ModFileGroup> _groups = [];
    private long _entryUntil;
    internal event Action? BackRequested;
    internal event Action<ModProjectRow, ModBrowserContext>? ProjectRequested;

    internal ModDetailsPage(ModProjectRow row, ModBrowserContext context)
    {
        _row = row;
        _context = context;
        InitializeComponent();
        ProjectCard.DataContext = row;
        WikiButton.DataContext = row;
        ProjectCard.ShowSummary = false;
        DescriptionText.Text = row.Summary;
        GameVersionBox.SetVersions((context.GameVersion is null ? [] : new[] { context.GameVersion }).Concat(row.Project.GameVersions.OrderDescending(ModPresentation.VersionOrder)), context.GameVersion);
        _lastVersion = GameVersionBox.Version ?? "";
        LoaderPair.IsExpanded = GameVersionBox.Version is not null;
        LoaderBox.ItemsSource = new[] { new FilterOption("", CommunityMods.L("all")), new("forge", "Forge"), new("neoforge", "NeoForge"), new("fabric", "Fabric"), new("quilt", "Quilt"), new("liteloader", "LiteLoader") };
        LoaderBox.SelectedIndex = Math.Max(0, Array.FindIndex((FilterOption[])LoaderBox.ItemsSource, option => option.Value == context.Loader));
        _ready = true;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        var enrichment = _row.EnrichAsync();
        foreach (var dependency in _groups.SelectMany(group => group.Dependencies ?? [])) _ = dependency.EnrichAsync();
        if (_filesReady)
        {
            foreach (var group in _groups.Where(group => group.IsExpanded)) _ = LoadDependenciesAsync(group);
            return;
        }

        var files = LoadFilesAsync();
        await enrichment;
        await files;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _request?.Cancel();
        CancelDependencies();
        _loaded = false;
    }

    private void CancelDependencies()
    {
        foreach (var request in _dependencyRequests.ToArray()) request.Cancel();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
    private void Source_Click(object sender, RoutedEventArgs e) => CommunityMods.Open(_row.Project.Website.AbsoluteUri);
    private void Wiki_Click(object sender, RoutedEventArgs e) => CommunityMods.Open(_row.WikiUri.AbsoluteUri);

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_row.Name);
            App.Logger.Information("Mods", $"已复制 Mods 名称: {_row.Name}.");
            LauncherToasts.Success(CommunityMods.L("nameCopied"));
        }
        catch (System.Runtime.InteropServices.COMException error)
        {
            App.Logger.Error("Mods", "复制 Mods 名称失败.", error);
            LauncherToasts.Error(CommunityMods.L("copyFailed"));
        }
    }

    private void Version_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) Dispatcher.BeginInvoke(() => UpdateVersion());
    }

    private void Version_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateVersion();

    private void Version_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = LoadFilesAsync();
        }
    }

    private void UpdateVersion()
    {
        if (_ready && (GameVersionBox.Version ?? "") != _lastVersion)
        {
            _lastVersion = GameVersionBox.Version ?? "";
            _ = LoadFilesAsync();
        }
    }

    private void Loader_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) _ = LoadFilesAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _fileCache.Clear();
        _ = LoadFilesAsync();
    }

    private async Task LoadFilesAsync()
    {
        _request?.Cancel();
        CancelDependencies();
        var request = new CancellationTokenSource();
        _request = request;
        _filesReady = false;
        _files.Clear();
        foreach (var old in _groups) old.PropertyChanged -= Group_Changed;
        _groups = [];
        FilesTimeline.ItemsSource = null;
        EmptyText.Visibility = Visibility.Visible;
        EmptyText.Text = CommunityMods.L("loading");
        string? version = GameVersionBox.Version;
        string? loader = (LoaderBox.SelectedItem as FilterOption)?.Value;
        if (version is null || string.IsNullOrEmpty(loader)) loader = null;
        LoaderPair.IsExpanded = version is not null;
        string cacheKey = (version ?? "") + ":" + (loader ?? "");
        App.Logger.Information("Mods", $"读取完整 Mods 文件列表: {_row.Project.Platform}/{_row.Project.Id}, 游戏={version}, 加载器={loader}.");
        try
        {
            IReadOnlyList<ModFile> all;
            if (_fileCache.TryGetValue(cacheKey, out var cached)) all = cached;
            else if (_fileCache.TryGetValue(":", out var complete))
            {
                all = complete.Where(file => (version is null || file.GameVersions.Contains(version)) && (loader is null || file.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))).ToArray();
                _fileCache[cacheKey] = all;
            }
            else
            {
                var client = CommunityMods.Client();
                object identity = request;
                var progress = new Progress<int>(count =>
                {
                    if (ReferenceEquals(_request, identity)) EmptyText.Text = string.Format(CommunityMods.L("filesLoading"), count);
                });
                all = await client.GetAllFilesAsync(_row.Project, version, loader, progress, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_request, request)) return;
                _fileCache[cacheKey] = all;
            }

            if (!ReferenceEquals(_request, request)) return;
            _files.AddRange(all);
            _filesReady = true;
            RefreshGroups();
            EmptyText.Text = CommunityMods.L("noFiles");
            EmptyText.Visibility = FilesTimeline.HasItems ? Visibility.Collapsed : Visibility.Visible;
            App.Logger.Information("Mods", $"完整 Mods 文件列表已读取: {_files.Count} 项.");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or TaskCanceledException)
        {
            if (!ReferenceEquals(_request, request)) return;
            EmptyText.Text = CommunityMods.L("filesFailed");
            App.Logger.Error("Mods", "读取完整 Mods 文件列表失败.", error);
            LauncherToasts.Error(CommunityMods.L("filesFailed"));
        }
        finally
        {
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }

    private void RefreshGroups()
    {
        var expanded = _groups.Where(group => group.IsExpanded).Select(group => group.Key).ToHashSet();
        foreach (var old in _groups) old.PropertyChanged -= Group_Changed;
        string? version = GameVersionBox.Version;
        string? loader = version is null ? null : (LoaderBox.SelectedItem as FilterOption)?.Value;
        _groups = ModFileGroup.Create(_files, version, string.IsNullOrEmpty(loader) ? null : loader);
        foreach (var group in _groups)
        {
            group.IsExpanded = expanded.Contains(group.Key);
            group.PropertyChanged += Group_Changed;
        }

        RebuildTimeline();
        foreach (var group in _groups.Where(group => group.IsExpanded)) _ = LoadDependenciesAsync(group);
    }

    private void Group_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ModFileGroup.IsExpanded) || sender is not ModFileGroup group) return;
        _entryUntil = Environment.TickCount64 + 240;
        RebuildTimeline();
        if (group.IsExpanded) _ = LoadDependenciesAsync(group);
    }

    private async Task LoadDependenciesAsync(ModFileGroup group)
    {
        if (group.RequiredDependencies.Count == 0 || group.Dependencies is not null || group.DependencyTask is not null) return;
        var request = new CancellationTokenSource();
        _dependencyRequests.Add(request);
        group.DependencyError = null;
        group.DependencyTask = ResolveDependenciesAsync(group, request.Token);
        try
        {
            group.Dependencies = await group.DependencyTask;
            foreach (var row in group.Dependencies) _ = row.EnrichAsync();
            App.Logger.Information("Mods", $"前置 Mods 已读取: {group.Title}, 数量={group.Dependencies.Count}.");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            group.DependencyTask = null;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or TaskCanceledException)
        {
            group.DependencyTask = null;
            group.DependencyError = CommunityMods.L("dependenciesFailed");
            App.Logger.Error("Mods", $"前置 Mods 读取失败: {group.Title}.", error);
        }
        finally
        {
            bool retry = request.IsCancellationRequested && _loaded && _filesReady;
            _dependencyRequests.Remove(request);
            request.Dispose();
            if (_groups.Contains(group))
            {
                RebuildTimeline();
                if (retry && group.IsExpanded) _ = LoadDependenciesAsync(group);
            }
        }
    }

    private async Task<IReadOnlyList<ModProjectRow>> ResolveDependenciesAsync(ModFileGroup group, CancellationToken token)
    {
        var projects = await CommunityMods.Client().GetDependenciesAsync(_row.Project.Platform, group.RequiredDependencies, token);
        return projects.Select(project => new ModProjectRow(project)).ToArray();
    }

    private void RebuildTimeline()
    {
        var scroll = FindScroll(FilesTimeline);
        double offset = scroll?.VerticalOffset ?? 0;
        object? anchor = null;
        double anchorTop = 0;
        if (FindPanel(FilesTimeline) is { } panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is not ListBoxItem container) continue;
                double top = container.TranslatePoint(new Point(), FilesTimeline).Y;
                if (top + container.ActualHeight <= 0) continue;
                anchor = container.DataContext;
                anchorTop = top;
                break;
            }
        }

        List<object> rows = [];
        foreach (var group in _groups)
        {
            rows.Add(group);
            if (!group.IsExpanded) continue;
            if (group.RequiredDependencies.Count > 0)
            {
                rows.Add(new ModSectionRow(CommunityMods.L("dependencies")));
                if (group.Dependencies is { } dependencies)
                    rows.AddRange(dependencies.Select(project => new ModDependencyRow(project)
                    {
                        Context = _context with { GameVersion = group.GameVersion, Loader = group.Loader }
                    }));
                else rows.Add(new ModSectionRow(group.DependencyError ?? CommunityMods.L("dependenciesLoading")));
            }

            rows.Add(new ModSectionRow(CommunityMods.L("filesSection")));
            rows.AddRange(group.Files);
            rows.Add(new ModGroupFooter());
        }

        FilesTimeline.ItemsSource = rows;
        FilesTimeline.UpdateLayout();
        if (anchor is not null && rows.Contains(anchor))
        {
            FilesTimeline.ScrollIntoView(anchor);
            FilesTimeline.UpdateLayout();
            if (FilesTimeline.ItemContainerGenerator.ContainerFromItem(anchor) is ListBoxItem container)
                scroll?.ScrollToVerticalOffset(scroll.VerticalOffset + container.TranslatePoint(new Point(), FilesTimeline).Y - anchorTop);
        }
        else scroll?.ScrollToVerticalOffset(offset);
    }

    private static VirtualizingStackPanel? FindPanel(DependencyObject target)
    {
        if (target is VirtualizingStackPanel panel) return panel;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(target); i++)
            if (FindPanel(VisualTreeHelper.GetChild(target, i)) is { } child)
                return child;
        return null;
    }

    private static ScrollViewer? FindScroll(DependencyObject target)
    {
        if (target is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(target); i++)
            if (FindScroll(VisualTreeHelper.GetChild(target, i)) is { } child)
                return child;
        return null;
    }

    private void TimelineRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || Environment.TickCount64 > _entryUntil) return;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        var offset = new TranslateTransform();
        element.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void Dependency_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModDependencyRow row }) ProjectRequested?.Invoke(row.Project, row.Context);
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileRow row }) return;
        try
        {
            string? initial = CommunityMods.DefaultModDirectory(_context);
            OpenFolderDialog picker = new() { Title = CommunityMods.L("downloadFolder"), Multiselect = false };
            if (initial is not null && Directory.Exists(initial)) picker.InitialDirectory = initial;
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            string directory = picker.FolderName;
            if (_context.VersionDirectory is { } versionRoot && string.Equals(Path.GetFullPath(directory), Path.GetFullPath(versionRoot), StringComparison.OrdinalIgnoreCase)) directory = Path.Combine(directory, "mods");

            Directory.CreateDirectory(directory);
            string? version = row.TargetGameVersion ?? GameVersionBox.Version;
            string? loader = row.TargetLoader ?? (LoaderBox.SelectedItem as FilterOption)?.Value;
            version ??= _context.GameVersion is { } currentVersion && row.File.GameVersions.Contains(currentVersion) ? currentVersion : row.File.GameVersions.FirstOrDefault();
            if (string.IsNullOrEmpty(loader)) loader = _context.Loader is { } currentLoader && row.File.Loaders.Contains(currentLoader, StringComparer.OrdinalIgnoreCase) ? currentLoader : row.File.Loaders.FirstOrDefault();
            if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(loader)) throw new InvalidOperationException(CommunityMods.L("chooseCompatibility"));
            var context = _context with { GameVersion = version, Loader = loader };
            ModInstallTasks.Start(_row.Project, row.File, context, directory);
            App.Logger.Information("Mods", $"创建 Mods 下载任务: {_row.Project.Name}, 文件={row.File.FileName}, 目录={directory}.");
            LauncherToasts.Information(CommunityMods.L("queued"));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            App.Logger.Error("Mods", "创建 Mods 下载任务失败.", error);
            LauncherToasts.Error(string.Format(CommunityMods.L("operationFailed"), error.Message));
        }
    }
}

public sealed class ModFileRow(ModFile file, string? gameVersion = null, string? loader = null)
{
    internal ModFile File { get; } = file;
    internal string? TargetGameVersion { get; } = gameVersion;
    internal string? TargetLoader { get; } = loader;
    public string Name => File.Name;
    public bool CanDownload => File.DownloadUri is not null;

    public Geometry ReleaseIcon => File.ReleaseType switch
    {
        "release" => LauncherIcons.VersionRelease,
        "beta" => LauncherIcons.VersionPreview,
        _ => LauncherIcons.Experimental
    };

    public string ReleaseLabel => CommunityMods.L("release." + File.ReleaseType);
    public string DownloadHint => CommunityMods.L(CanDownload ? "download" : "downloadUnavailable");
    public string Metadata => $"{CommunityMods.L("release." + File.ReleaseType)} · {File.Published?.ToLocalTime():yyyy-MM-dd} · {DataSizeFormatter.Format(File.Size, App.Settings.MemoryUnit, "0.##", minimumPower: 0)} · {File.FileName}";
}
