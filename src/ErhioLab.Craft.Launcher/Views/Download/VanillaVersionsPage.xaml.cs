using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Download;

/// <summary>展示从 Mojang 或镜像获取的原版游戏版本.</summary>
public partial class VanillaVersionsPage
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly MinecraftVersionCatalogClient _catalogClient = new(HttpClient);
    private readonly MinecraftWikiClient _wikiClient = new(HttpClient);
    private readonly Dictionary<FrameworkElement, int> _groupAnimationVersions = [];
    private CancellationTokenSource? _pageCancellation;
    private bool _hasLoaded;
    private bool _loading;
    private VersionListSource? _loadedPreference;

    /// <summary>创建版本列表页面, 网络请求在页面首次显示时开始.</summary>
    public VanillaVersionsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _pageCancellation = new CancellationTokenSource();
        if (!_hasLoaded || _loadedPreference != App.Settings.VersionListSource)
        {
            _hasLoaded = true;
            await LoadVersionsAsync();
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
    }

    /// <summary>由下载侧栏的悬浮按钮触发重新获取.</summary>
    internal void RefreshVersions() => _ = LoadVersionsAsync();

    private async Task LoadVersionsAsync()
    {
        if (_pageCancellation is null || _loading)
        {
            return;
        }

        _loading = true;
        CancellationToken cancellationToken = _pageCancellation.Token;
        _loadedPreference = App.Settings.VersionListSource;
        LauncherToasts.Information("正在获取游戏版本列表...");
        try
        {
            MinecraftManifestSource preferred = App.Settings.VersionListSource == VersionListSource.MirrorFirst ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
            App.Logger.Information("Download", $"开始获取版本列表, 首选来源: {preferred}.");
            MinecraftVersionCatalog catalog = await _catalogClient.GetCatalogAsync(preferred, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            VersionGroups.ItemsSource = CreateGroups(catalog);
            App.Logger.Information("Download", $"版本列表获取完成, 实际来源: {catalog.Source}, 数量: {catalog.Versions.Count}.");
            LauncherToasts.Success(catalog.Source == preferred ? "游戏版本列表已获取." : $"已从{SourceName(catalog.Source)}获取游戏版本列表.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _hasLoaded = false;
        }
        catch (Exception error)
        {
            App.Logger.Error("Download", "版本列表获取失败.", error);
            VersionGroups.ItemsSource = null;
            LauncherToasts.Error("获取游戏版本失败, 请检查网络并在左侧刷新重试.");
        }
        finally
        {
            _loading = false;
        }
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
        LauncherToasts.Information($"已选择 {row.Version.Id}, 版本下载功能将在后续提供.");
    }

    private static IReadOnlyList<VersionGroup> CreateGroups(MinecraftVersionCatalog catalog)
    {
        MinecraftVersion[] latest = catalog.Versions
            .Where(version => version.Id == catalog.LatestReleaseId || version.Id == catalog.LatestPreviewId)
            .OrderBy(version => version.Id == catalog.LatestReleaseId ? 0 : 1)
            .ToArray();
        return
        [
            new VersionGroup("最新版本", true, latest.Select(version => new VersionRow(version, version.Id == catalog.LatestReleaseId ? "最新正式版" : "最新预览版")).ToArray()),
            Group("正式版", MinecraftVersionKind.Release),
            Group("预览版", MinecraftVersionKind.Preview),
            Group("远古版", MinecraftVersionKind.Ancient),
            Group("愚人节版", MinecraftVersionKind.AprilFools)
        ];

        VersionGroup Group(string title, MinecraftVersionKind kind) => new(title, false, catalog.Versions.Where(version => version.Kind == kind).Select(version => new VersionRow(version, null)).ToArray());
    }

    private async void ChangelogButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: VersionRow row } || !row.MarkSummaryRequested() || _pageCancellation is null)
        {
            return;
        }

        try
        {
            MinecraftWikiPage? page = await _wikiClient.GetSummaryAsync(row.Version.Id, _pageCancellation.Token);
            row.TooltipText = page is null ? "Minecraft Wiki 暂无该版本的更新摘要." : page.Text.Length > 700 ? page.Text[..700] + "..." : page.Text;
        }
        catch (OperationCanceledException)
        {
            row.ResetSummaryRequest();
        }
        catch (Exception error)
        {
            row.ResetSummaryRequest();
            row.TooltipText = "暂时无法获取 Minecraft Wiki 更新摘要.";
            App.Logger.Error("Wiki", $"获取 {row.Version.Id} 更新摘要失败.", error);
        }
    }

    private void ChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VersionRow row })
        {
            return;
        }

        try
        {
            Uri pageUrl = MinecraftWikiClient.GetPageUrl(row.Version.Id);
            Process.Start(new ProcessStartInfo(pageUrl.AbsoluteUri) { UseShellExecute = true });
            App.Logger.Information("Wiki", $"在浏览器打开 {row.Version.Id} 的更新日志.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("Wiki", $"无法在浏览器打开 {row.Version.Id} 的更新日志.", error);
            LauncherToasts.Error("无法打开 Minecraft Wiki 页面.");
        }
    }

    private static string SourceName(MinecraftManifestSource source) => source == MinecraftManifestSource.Official ? "官方源" : "镜像源";

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ELCL/0.1");
        return client;
    }

    private sealed record VersionGroup(string Title, bool IsInitiallyExpanded, IReadOnlyList<VersionRow> Versions);

    private sealed class VersionRow : INotifyPropertyChanged
    {
        private string _tooltipText = "正在从 Minecraft Wiki 获取更新摘要...";
        private bool _summaryRequested;

        internal VersionRow(MinecraftVersion version, string? label)
        {
            Version = version;
            Description = $"{label ?? KindName(version.Kind)}, 发布于 {version.ReleaseTime.ToLocalTime():yyyy/MM/dd HH:mm}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MinecraftVersion Version { get; }

        public Geometry Icon => Version.Kind switch
        {
            MinecraftVersionKind.Release => LauncherIcons.VersionRelease,
            MinecraftVersionKind.Preview => LauncherIcons.VersionPreview,
            MinecraftVersionKind.Ancient => LauncherIcons.VersionAncient,
            _ => LauncherIcons.VersionAprilFools
        };

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
            MinecraftVersionKind.Release => "正式版",
            MinecraftVersionKind.Preview => "预览版",
            MinecraftVersionKind.Ancient => "远古版",
            _ => "愚人节版"
        };
    }
}
