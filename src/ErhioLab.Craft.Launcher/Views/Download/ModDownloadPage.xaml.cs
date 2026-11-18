using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Views.Download;

public partial class ModDownloadPage
{
    private bool _ready;
    private bool _initialized;
    private bool _loading;
    private int _page;
    private CancellationTokenSource? _search;
    private ModBrowserContext? _context;
    private bool _explicitContext;
    private ModSearchPageCache? _pageCache;
    private ModSearchRequest? _cachedQuery;
    private CommunitySource _cachedSource;
    private CancellationTokenSource? _prefetch;
    private ModSearchRequest? _lastRequest;
    private ModSearchResult? _lastResult;
    internal event Action<ModProjectRow, ModBrowserContext>? ProjectSelected;

    public ModDownloadPage()
    {
        InitializeComponent();
        SourcesBox.SelectedIndex = 0;
        GameVersionBox.SetVersions(new[] { "26.3", "26.2", "26.1.2", "1.21.11", "1.21.10", "1.21.8", "1.21.5", "1.21.4", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.19.2", "1.18.2", "1.16.5", "1.12.2", "1.8.9", "1.7.10" });
        LoaderBox.ItemsSource = new[] { new FilterOption("", CommunityMods.L("all")), new("forge", "Forge"), new("neoforge", "NeoForge"), new("fabric", "Fabric"), new("quilt", "Quilt"), new("liteloader", "LiteLoader") };
        LoaderBox.SelectedIndex = 0;
        CategoryBox.ItemsSource = Categories();
        CategoryBox.SelectedIndex = 0;
        SortBox.SelectedIndex = 0;
        _ready = true;
    }

    internal void SetContext(ModBrowserContext context)
    {
        _search?.Cancel();
        ResetPageCache();
        _context = context;
        _explicitContext = true;
        _ready = false;
        GameVersionBox.Version = context.GameVersion;
        LoaderBox.SelectedIndex = Array.FindIndex((FilterOption[])LoaderBox.ItemsSource, option => option.Value == context.Loader);
        if (LoaderBox.SelectedIndex < 0) LoaderBox.SelectedIndex = 0;
        LoaderPair.IsExpanded = !string.IsNullOrWhiteSpace(context.GameVersion);
        _ready = true;
        _initialized = false;
        _page = 0;
        if (IsLoaded)
        {
            _initialized = true;
            _ = SearchAsync();
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ResultsList.ItemsSource is IEnumerable<ModProjectRow> rows)
            foreach (var row in rows) _ = row.EnrichAsync();
        if (_initialized && (_explicitContext || _context?.VersionDirectory == App.Settings.SelectedVersionPath))
        {
            if (_pageCache is not null && _cachedSource != CommunityMods.Source) Refresh();
            else if (_lastRequest is { } request && _lastResult is { } result) StartPrefetch(request, result);
            return;
        }
        _initialized = true;
        // 元数据扫描在后台执行, 不阻塞导航动画.
        if (_context is null || !_explicitContext && _context.VersionDirectory != App.Settings.SelectedVersionPath)
        {
            SetContext(await Task.Run(ModBrowserContext.Current));
            _explicitContext = false;
        }
        else await SearchAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelPrefetch();
        _pageCache?.CancelPending();
        if (_loading)
        {
            _search?.Cancel();
            _initialized = false;
        }
    }

    internal void Refresh()
    {
        ResetPageCache();
        LoaderPair.IsExpanded = GameVersionBox.Version is not null;
        _page = 0;
        _ = SearchAsync();
    }

    private void Search_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Query_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Refresh();
        }
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && IsLoaded) Refresh();
    }

    private void Version_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        // 可编辑 ComboBox 的 Text 在 SelectionChanged 后更新.
        Dispatcher.BeginInvoke(() => UpdateVersion());
    }

    private void Version_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateVersion();
    private string _lastVersion = "";

    private void UpdateVersion()
    {
        if (!_ready) return;
        string version = GameVersionBox.Version ?? "";
        LoaderPair.IsExpanded = version.Length > 0;
        if (version != _lastVersion)
        {
            _lastVersion = version;
            if (IsLoaded) Refresh();
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (!_loading && _page > 0)
        {
            _page--;
            _ = SearchAsync();
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _page >= 499 || !NextButton.IsEnabled) return;
        _page++;
        _ = SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (!_ready) return;
        CancelPrefetch();
        _search?.Cancel();
        var cancellation = new CancellationTokenSource();
        _search = cancellation;
        _loading = true;
        SearchButton.IsEnabled = PreviousButton.IsEnabled = NextButton.IsEnabled = false;
        ResultsList.ItemsSource = null;
        EmptyText.Visibility = Visibility.Visible;
        EmptyText.Text = CommunityMods.L("loading");
        ResultStatus.Text = "";
        PageText.Text = string.Format(CommunityMods.L("page"), _page + 1);
        string? version = GameVersionBox.Version;
        string? loader = version is null || LoaderBox.SelectedItem is not FilterOption { Value.Length: > 0 } selected ? null : selected.Value;
        var category = (CategoryBox.SelectedItem as CategoryOption)?.Category;
        string query = QueryBox.Text.Trim();
        ModPlatform platform = (ModPlatform)Math.Max(0, SourcesBox.SelectedIndex);
        ModSortOrder sort = SortBox.SelectedIndex == 1 ? ModSortOrder.Downloads : ModSortOrder.Relevance;
        var queryRequest = new ModSearchRequest(query, platform, version, loader, category, SortOrder: sort);
        if (_pageCache is null || _cachedQuery != queryRequest || _cachedSource != CommunityMods.Source)
        {
            ResetPageCache();
            var client = CommunityMods.Client();
            _pageCache = new ModSearchPageCache(client.SearchAsync);
            _cachedQuery = queryRequest;
            _cachedSource = CommunityMods.Source;
        }
        var cache = _pageCache;
        App.Logger.Information("Mods", $"查询 Mods: 来源={platform}, 排序={sort}, 游戏={version}, 加载器={loader}, 类别={category?.Id}, 页={_page + 1}.");
        try
        {
            if (CommunityMods.IsTranslationEnabled && query.Any(character => character is >= '\u4e00' and <= '\u9fff'))
            {
                var translation = await CommunityMods.TranslationAsync(query);
                cancellation.Token.ThrowIfCancellationRequested();
                if (translation is { EnglishName.Length: > 0 }) query = translation.EnglishName;
            }

            var request = queryRequest with { Query = query, Page = _page };
            var result = await cache.GetAsync(request).WaitAsync(cancellation.Token);
            if (!ReferenceEquals(_search, cancellation)) return;
            foreach (var failure in result.SourceErrors) App.Logger.Error("Mods", $"Mods 来源暂不可用: {failure.Key}.", failure.Value);
            var rows = result.Projects.Select(project => new ModProjectRow(project)).ToArray();
            ResultsList.ItemsSource = rows;
            if (rows.Length > 0) ResultsList.ScrollIntoView(rows[0]);
            EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = CommunityMods.L("empty");
            ResultStatus.Text = result.FailedPlatforms.Count > 0
                ? string.Format(CommunityMods.L("partialFailure"), string.Join("、", result.FailedPlatforms))
                : string.Format(CommunityMods.L("results"), result.Total);
            PreviousButton.IsEnabled = _page > 0;
            NextButton.IsEnabled = result.HasMore && _page < 499;
            foreach (var row in rows) _ = row.EnrichAsync();
            _lastRequest = request;
            _lastResult = result;
            if (IsLoaded) StartPrefetch(request, result);
            App.Logger.Information("Mods", $"Mods 查询完成: 条目={rows.Length}, 总数={result.Total}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or AggregateException or InvalidOperationException or KeyNotFoundException or FormatException or TaskCanceledException)
        {
            if (!ReferenceEquals(_search, cancellation)) return;
            EmptyText.Text = CommunityMods.L("searchFailed");
            App.Logger.Error("Mods", "Mods 查询失败.", error);
            LauncherToasts.Error(CommunityMods.L("searchFailed"));
        }
        finally
        {
            if (ReferenceEquals(_search, cancellation))
            {
                _loading = false;
                SearchButton.IsEnabled = true;
                PreviousButton.IsEnabled = _page > 0;
            }

            cancellation.Dispose();
            if (ReferenceEquals(_search, cancellation)) _search = null;
        }
    }

    private void CancelPrefetch()
    {
        _prefetch?.Cancel();
        _prefetch = null;
    }

    private void ResetPageCache()
    {
        CancelPrefetch();
        _pageCache?.Dispose();
        _pageCache = null;
        _cachedQuery = null;
        _lastRequest = null;
        _lastResult = null;
    }

    private void StartPrefetch(ModSearchRequest request, ModSearchResult result)
    {
        CancelPrefetch();
        if (!result.HasMore || request.Page >= 499 || _pageCache is not { } cache) return;
        var cancellation = new CancellationTokenSource();
        _prefetch = cancellation;
        _ = PrefetchAsync(cache, request with { Page = request.Page + 1 }, cancellation);
    }

    private async Task PrefetchAsync(ModSearchPageCache cache, ModSearchRequest request, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            App.Logger.Information("Mods", $"开始预加载 Mods 第 {request.Page + 1} 页.");
            var result = await cache.GetAsync(request).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            foreach (var failure in result.SourceErrors)
                App.Logger.Error("Mods", $"预加载 Mods 来源暂不可用: {failure.Key}.", failure.Value);
            await Parallel.ForEachAsync(result.Projects,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
                async (project, assetToken) => await Task.WhenAll(CommunityMods.ImageAsync(project.Icon),
                    CommunityMods.TranslationAsync(project.Name, project)).WaitAsync(assetToken));
            App.Logger.Information("Mods", $"Mods 第 {request.Page + 1} 页预加载完成: {result.Projects.Count} 项.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            App.Logger.Information("Mods", $"Mods 第 {request.Page + 1} 页预加载已取消.");
        }
        catch (Exception error)
        {
            App.Logger.Error("Mods", $"Mods 第 {request.Page + 1} 页预加载失败, 翻页时重新尝试.", error);
        }
        finally
        {
            if (ReferenceEquals(_prefetch, cancellation)) _prefetch = null;
            cancellation.Dispose();
        }
    }

    private void Result_Click(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ModProjectRow row && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ResultsList, source) is ListBoxItem)
            Open(row);
    }

    private void Result_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.SelectedItem is ModProjectRow row)
        {
            e.Handled = true;
            Open(row);
        }
    }

    private void Open(ModProjectRow row)
    {
        string? version = GameVersionBox.Version;
        string? loader = version is null ? null : (LoaderBox.SelectedItem as FilterOption)?.Value;
        ProjectSelected?.Invoke(row, new ModBrowserContext(version, string.IsNullOrEmpty(loader) ? null : loader, _context?.VersionDirectory));
    }

    private static CategoryOption[] Categories() =>
    [
        new(null, CommunityMods.L("all")),
        Category("adventure", "adventure", 422), Category("technology", "technology", 412), Category("magic", "magic", 419),
        Category("worldgen", "worldgen", 406), Category("biomes", "worldgen", 407), Category("structures", "worldgen", 409),
        Category("dimensions", "worldgen", 410), Category("mobs", "mobs", 411), Category("equipment", "equipment", 434),
        Category("food", "food", 436), Category("farming", "", 416), Category("storage", "storage", 420),
        Category("transportation", "transportation", 414), Category("energy", "technology", 417), Category("automation", "technology", 4843),
        Category("decoration", "decoration", 424), Category("optimization", "optimization", 6814), Category("utility", "utility", 5191),
        Category("library", "library", 421), Category("management", "management", 435), Category("map", "utility", 423),
        Category("ores", "worldgen", 408), Category("processing", "technology", 413), Category("transport", "transportation", 415),
        Category("genetics", "", 418), Category("redstone", "technology", 4558), Category("bugfixes", "optimization", 6821), Category("creative", "", 9026),
        new(new ModCategory("cursed", "cursed"), CommunityMods.L("category.cursed")),
        new(new ModCategory("economy", "economy"), CommunityMods.L("category.economy")),
        new(new ModCategory("gameMechanics", "game-mechanics"), CommunityMods.L("category.gameMechanics")),
        new(new ModCategory("minigame", "minigame"), CommunityMods.L("category.minigame")),
        new(new ModCategory("social", "social"), CommunityMods.L("category.social")), Category("misc", "", 425)
    ];

    private static CategoryOption Category(string key, string tag, int cf) => new(new ModCategory(key, tag, cf), CommunityMods.L("category." + key));
}

internal sealed record FilterOption(string Value, string Label)
{
    public override string ToString() => Label;
}

internal sealed record CategoryOption(ModCategory? Category, string Label)
{
    public override string ToString() => Label;
}
