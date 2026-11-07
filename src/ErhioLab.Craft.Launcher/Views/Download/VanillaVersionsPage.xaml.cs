using ErhioLab.Craft.Launcher.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Download;

/// <summary>展示从 Mojang 或镜像获取的原版游戏版本.</summary>
public partial class VanillaVersionsPage
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly MinecraftVersionCatalogClient _catalogClient = new(HttpClient);
    private readonly JsonFileStore<MinecraftVersionCatalog> _catalogCache = new(Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "version_catalog.json"));
    private readonly Dictionary<FrameworkElement, int> _groupAnimationVersions = [];
    private readonly CancellationTokenSource _pageCancellation = new();
    private MinecraftVersionCatalog? _catalog;
    private bool _hasLoaded;
    private bool _loading;
    private bool _closeHooked;
    private VersionListSource? _loadedPreference;

    /// <summary>用户选择一个游戏版本时, 请求打开下载详情页.</summary>
    internal event Action<MinecraftVersion>? VersionSelected;

    /// <summary>创建版本列表页面, 首次显示时先读取缓存, 再检查网络更新.</summary>
    public VanillaVersionsPage()
    {
        InitializeComponent();
        LocalizationManager.Changed += (_, _) => ApplySearch();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_closeHooked && Window.GetWindow(this) is { } window)
        {
            window.Closed += PageWindow_Closed;
            _closeHooked = true;
        }

        if (!_hasLoaded)
        {
            _hasLoaded = true;
            TryLoadCache();
            await LoadVersionsAsync(false);
        }
        else if (_loadedPreference != App.Settings.VersionListSource)
        {
            await LoadVersionsAsync(false);
        }
    }

    private void PageWindow_Closed(object? sender, EventArgs e)
    {
        _pageCancellation.Cancel();
        _pageCancellation.Dispose();
    }

    /// <summary>由下载侧栏的悬浮按钮触发重新获取.</summary>
    internal void RefreshVersions() => _ = LoadVersionsAsync(true);

    private void TryLoadCache()
    {
        try
        {
            if (!File.Exists(_catalogCache.FilePath))
            {
                return;
            }

            MinecraftVersionCatalog cached = _catalogCache.Load(new MinecraftVersionCatalog(
                MinecraftManifestSource.Official, string.Empty, string.Empty, []));
            if (!IsValidCatalog(cached))
            {
                throw new JsonException(LocalizationManager.Get("versionCatalog.cacheIncomplete"));
            }

            _catalog = cached;
            ApplySearch();
            App.Logger.Information("Download", $"已从缓存加载 {cached.Versions.Count} 个游戏版本.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            App.Logger.Error("Download", "读取游戏版本缓存失败, 将尝试联网获取.", error);
        }
    }

    private async Task LoadVersionsAsync(bool manualRefresh)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        CancellationToken cancellationToken = _pageCancellation.Token;
        _loadedPreference = App.Settings.VersionListSource;
        if (manualRefresh || _catalog is null)
        {
            LauncherToasts.Information(LocalizationManager.Get("versionCatalog.loading"));
        }
        try
        {
            MinecraftManifestSource preferred = App.Settings.VersionListSource == VersionListSource.MirrorFirst ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
            App.Logger.Information("Download", $"开始获取版本列表, 首选来源: {preferred}.");
            MinecraftVersionCatalog catalog = await _catalogClient.GetCatalogAsync(preferred, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            bool changed = _catalog is null || !CatalogsEqual(_catalog, catalog);
            if (changed)
            {
                _catalog = catalog;
                ApplySearch();
                TrySaveCache(catalog);
            }
            App.Logger.Information("Download", $"版本列表获取完成, 实际来源: {catalog.Source}, 数量: {catalog.Versions.Count}.");
            if (changed || manualRefresh)
            {
                LauncherToasts.Success(changed ? LocalizationManager.Get("versionCatalog.updated") : LocalizationManager.Get("versionCatalog.upToDate"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _hasLoaded = false;
        }
        catch (Exception error)
        {
            App.Logger.Error("Download", "版本列表获取失败.", error);
            LauncherToasts.Error(_catalog is null
                ? LocalizationManager.Get("versionCatalog.fetchFailed")
                : LocalizationManager.Get("versionCatalog.updateFailedUsingCache"));
        }
        finally
        {
            _loading = false;
        }
    }

    private void TrySaveCache(MinecraftVersionCatalog catalog)
    {
        try
        {
            _catalogCache.Save(catalog);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Download", "保存游戏版本缓存失败.", error);
        }
    }

    private static bool IsValidCatalog(MinecraftVersionCatalog? catalog) =>
        catalog is { Versions.Count: > 0 } &&
        !string.IsNullOrWhiteSpace(catalog.LatestReleaseId) &&
        !string.IsNullOrWhiteSpace(catalog.LatestPreviewId) &&
        catalog.Versions.Any(version => version.Id == catalog.LatestReleaseId) &&
        catalog.Versions.Any(version => version.Id == catalog.LatestPreviewId);

    private static bool CatalogsEqual(MinecraftVersionCatalog oldCatalog, MinecraftVersionCatalog newCatalog) =>
        oldCatalog.LatestReleaseId == newCatalog.LatestReleaseId &&
        oldCatalog.LatestPreviewId == newCatalog.LatestPreviewId &&
        oldCatalog.Versions.Count == newCatalog.Versions.Count &&
        oldCatalog.Versions.Zip(newCatalog.Versions).All(pair =>
            pair.First.Id == pair.Second.Id &&
            pair.First.MetadataUrl == pair.Second.MetadataUrl &&
            pair.First.Sha1 == pair.Second.Sha1);

    private void VersionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is null || ClearSearchButton is null)
        {
            return;
        }

        bool hasText = VersionSearchBox.Text.Length > 0;
        SearchHint.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        ApplySearch();
    }

    private void VersionSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            VersionSearchBox.Clear();
            e.Handled = true;
        }
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        VersionSearchBox.Clear();
        VersionSearchBox.Focus();
    }

    private void ApplySearch()
    {
        if (_catalog is null || VersionGroups is null || EmptySearchText is null)
        {
            return;
        }

        string query = VersionSearchBox.Text.Trim();
        _groupAnimationVersions.Clear();
        if (query.Length == 0)
        {
            EmptySearchText.Visibility = Visibility.Collapsed;
            VersionGroups.ItemsSource = CreateGroups(_catalog);
            return;
        }

        VersionRow[] matches = _catalog.Versions
            .Where(version => version.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(version => new VersionRow(version, null))
            .ToArray();
        EmptySearchText.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        VersionGroups.ItemsSource = matches.Length == 0 ? Array.Empty<VersionGroup>() : new[] { new VersionGroup(string.Format(LocalizationManager.Get("versions.searchResults"), matches.Length), true, matches) };
    }

    private void GroupContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VersionGroup group } content)
        {
            content.Height = group.IsInitiallyExpanded ? double.NaN : 0;
        }
    }

    private void GroupHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: FrameworkElement content } header)
        {
            return;
        }

        bool expanding = header.IsChecked == true;
        double currentHeight = content.ActualHeight;
        content.BeginAnimation(HeightProperty, null);
        content.Height = double.NaN;
        content.Measure(new Size(Math.Max(content.ActualWidth, 100), double.PositiveInfinity));
        double targetHeight = expanding ? content.DesiredSize.Height : 0;
        content.Height = currentHeight;

        int version = _groupAnimationVersions.GetValueOrDefault(content) + 1;
        _groupAnimationVersions[content] = version;
        DoubleAnimation animation = new(currentHeight, targetHeight, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (_groupAnimationVersions.GetValueOrDefault(content) == version)
            {
                content.Height = expanding ? double.NaN : 0;
            }
        };
        content.BeginAnimation(HeightProperty, animation);
    }

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: VersionRow row })
        {
            return;
        }

        App.Logger.Information("Download", $"选择游戏版本: {row.Version.Id}.");
        ((ListBox)sender).SelectedItem = null;
        VersionSelected?.Invoke(row.Version);
    }

    private static IReadOnlyList<VersionGroup> CreateGroups(MinecraftVersionCatalog catalog)
    {
        MinecraftVersion[] latest = catalog.Versions
            .Where(version => version.Id == catalog.LatestReleaseId || version.Id == catalog.LatestPreviewId)
            .OrderBy(version => version.Id == catalog.LatestReleaseId ? 0 : 1)
            .ToArray();
        return
        [
            new VersionGroup(LocalizationManager.Get("versionCatalog.latestVersions"), true, latest.Select(version => new VersionRow(version, version.Id == catalog.LatestReleaseId ? LocalizationManager.Get("versionCatalog.latestRelease") : LocalizationManager.Get("versionCatalog.latestSnapshot"))).ToArray()),
            Group(LocalizationManager.Get("version.release"), MinecraftVersionKind.Release),
            Group(LocalizationManager.Get("versionCatalog.snapshotCategory"), MinecraftVersionKind.Preview),
            Group(LocalizationManager.Get("version.oldVersions"), MinecraftVersionKind.Ancient),
            Group(LocalizationManager.Get("versionCatalog.aprilFoolsVersions"), MinecraftVersionKind.AprilFools)
        ];

        VersionGroup Group(string title, MinecraftVersionKind kind) => new(title, false, catalog.Versions.Where(version => version.Kind == kind).Select(version => new VersionRow(version, null)).ToArray());
    }

    private async void ChangelogButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: VersionRow row } || !row.MarkSummaryRequested() || _pageCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            string language = LocalizationManager.Get("wiki.lang");
            MinecraftWikiClient wikiClient = new(HttpClient, language: language);
            MinecraftWikiPage? page = await wikiClient.GetSummaryAsync(row.Version.Id, _pageCancellation.Token);
            row.WikiPageUrl = page?.Url;
            row.WikiLanguage = language;
            row.TooltipText = page is null ? LocalizationManager.Get("versionCatalog.noWikiSummary") : page.Text.Length > 700 ? page.Text[..700] + "..." : page.Text;
            if (page is not null && UsesEnglishFallback(page.Url, language))
                row.TooltipText = LocalizationManager.Get("versionCatalog.wikiSummaryEnglishFallback") + "\n\n" + row.TooltipText;
        }
        catch (OperationCanceledException)
        {
            row.ResetSummaryRequest();
        }
        catch (Exception error)
        {
            row.ResetSummaryRequest();
            row.TooltipText = LocalizationManager.Get("versionCatalog.wikiSummaryFailed");
            App.Logger.Error("Wiki", $"获取 {row.Version.Id} 更新摘要失败.", error);
        }
    }

    private async void ChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VersionRow row } button || _pageCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            button.IsEnabled = false;
            string language = LocalizationManager.Get("wiki.lang");
            App.Logger.Information("Wiki", $"开始解析 {row.Version.Id} 的 Wiki 页面, 优先语言: {language}.");
            Uri? pageUrl = row.WikiLanguage == language ? row.WikiPageUrl : null;
            if (pageUrl is null)
            {
                LauncherToasts.Information(LocalizationManager.Get("versionCatalog.openingWiki"));
                MinecraftWikiClient wikiClient = new(HttpClient, language: language);
                pageUrl = await wikiClient.GetPageUrlAsync(row.Version.Id, _pageCancellation.Token);
            }
            if (_pageCancellation.IsCancellationRequested) return;
            if (pageUrl is null)
            {
                App.Logger.Warning("Wiki", $"未找到 {row.Version.Id} 的 Wiki 页面.");
                LauncherToasts.Error(LocalizationManager.Get("versionCatalog.noWikiPage"));
                return;
            }
            row.WikiPageUrl = pageUrl;
            row.WikiLanguage = language;
            Process.Start(new ProcessStartInfo(pageUrl.AbsoluteUri) { UseShellExecute = true });
            App.Logger.Information("Wiki", $"在浏览器打开 {row.Version.Id} 的更新日志: {pageUrl}.");
            LauncherToasts.Success(LocalizationManager.Get(UsesEnglishFallback(pageUrl, language)
                ? "versionCatalog.wikiOpenedEnglishFallback" : "versionCatalog.wikiOpened"));
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
            // 页面已关闭, 不再打开浏览器或显示通知.
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or HttpRequestException or JsonException or ArgumentException or OperationCanceledException)
        {
            App.Logger.Error("Wiki", $"无法在浏览器打开 {row.Version.Id} 的更新日志.", error);
            LauncherToasts.Error(LocalizationManager.Get("versionCatalog.openWikiFailed"));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static bool UsesEnglishFallback(Uri pageUrl, string language) =>
        pageUrl.Host.Equals("minecraft.wiki", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(language) && !language.Trim().Equals("en", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ELCL/0.1");
        return client;
    }

    internal sealed record VersionGroup(string Title, bool IsInitiallyExpanded, IReadOnlyList<VersionRow> Versions);

    internal sealed class VersionRow : INotifyPropertyChanged
    {
        private string _tooltipText = LocalizationManager.Get("versionCatalog.loadingWikiSummary");
        private bool _summaryRequested;

        internal VersionRow(MinecraftVersion version, string? label)
        {
            Version = version;
            Description = string.Format(LocalizationManager.Get("versions.description"), label ?? KindName(version.Kind), version.ReleaseTime.ToLocalTime());
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MinecraftVersion Version { get; }

        internal Uri? WikiPageUrl { get; set; }

        internal string? WikiLanguage { get; set; }

        public Geometry Icon => LauncherIcons.ForVersion(Version.Kind);

        public string Description { get; }

        public string TooltipText
        {
            get => _tooltipText;
            set
            {
                _tooltipText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TooltipText)));
            }
        }

        internal bool MarkSummaryRequested()
        {
            if (_summaryRequested)
            {
                return false;
            }

            _summaryRequested = true;
            return true;
        }

        internal void ResetSummaryRequest() => _summaryRequested = false;

        private static string KindName(MinecraftVersionKind kind) => kind switch
        {
            MinecraftVersionKind.Release => LocalizationManager.Get("version.release"),
            MinecraftVersionKind.Preview => LocalizationManager.Get("versionCatalog.snapshotCategory"),
            MinecraftVersionKind.Ancient => LocalizationManager.Get("version.oldVersions"),
            _ => LocalizationManager.Get("versionCatalog.aprilFoolsVersions")
        };
    }
}
