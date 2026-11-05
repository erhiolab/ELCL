using ErhioLab.Craft.Launcher.Localization;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Download;

/// <summary>展示原版游戏版本的下载前设置, 下载功能将在后续接入.</summary>
public partial class VanillaVersionDetailsPage
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly MinecraftVersion _version;
    private readonly MinecraftLoaderVersionClient _loaderClient = new(HttpClient);
    private readonly CancellationTokenSource _pageCancellation = new();
    private readonly Dictionary<LoaderSection, (ToggleButton Header, FrameworkElement Content)> _sectionControls = [];
    private readonly Dictionary<FrameworkElement, int> _animationVersions = [];
    private Window? _ownerWindow;
    private bool _started;
    private bool _updatingSelections;

    /// <summary>页面上的加载器分组及其获取状态.</summary>
    public IReadOnlyList<LoaderSection> LoaderSections { get; } =
    [
        new(MinecraftLoaderKind.Forge, "Forge"),
        new(MinecraftLoaderKind.NeoForge, "NeoForge"),
        new(MinecraftLoaderKind.Fabric, "Fabric"),
        new(MinecraftLoaderKind.Quilt, "Quilt"),
        new(MinecraftLoaderKind.LiteLoader, "LiteLoader"),
        new(MinecraftLoaderKind.OptiFine, "OptiFine")
    ];

    /// <summary>请求返回原版游戏版本列表.</summary>
    internal event Action? BackRequested;

    /// <summary>创建指定游戏版本的下载详情页.</summary>
    internal VanillaVersionDetailsPage(MinecraftVersion version)
    {
        _version = version;
        InitializeComponent();
        LocalizationManager.Changed += LocalizationChanged;
        VersionIcon.Data = LauncherIcons.ForVersion(version.Kind);
        VersionNameBox.Text = version.Id;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow is not null)
        {
            _ownerWindow.Closed += OwnerWindow_Closed;
        }

        foreach (LoaderSection section in LoaderSections)
        {
            _ = LoadSectionAsync(section);
        }
    }

    private async Task LoadSectionAsync(LoaderSection section)
    {
        if (section.IsLoading || _pageCancellation.IsCancellationRequested)
        {
            return;
        }

        section.SetLoading();
        MinecraftManifestSource preferred = App.Settings.VersionListSource == VersionListSource.MirrorFirst ? MinecraftManifestSource.Mirror : MinecraftManifestSource.Official;
        App.Logger.Information("Download", $"开始获取 {_version.Id} 的 {section.Kind} 版本列表, 首选来源: {preferred}.");
        try
        {
            MinecraftLoaderVersionCatalog catalog = await _loaderClient.GetVersionsAsync(
                section.Kind, _version.Id, preferred, _pageCancellation.Token);
            if (_pageCancellation.IsCancellationRequested)
            {
                return;
            }

            section.SetVersions(catalog.Versions);
            RefreshCompatibility();
            App.Logger.Information("Download", $"{section.Kind} 版本列表已获取, 来源: {catalog.Source}, 数量: {catalog.Versions.Count}.");
            if (catalog.Versions.Count > 0 && section.Kind == MinecraftLoaderKind.Forge)
            {
                _ = FillForgeReleaseTimesAsync(section, catalog);
            }
            else if (catalog.Versions.Count > 0 && section.Kind == MinecraftLoaderKind.Fabric)
            {
                _ = FillFabricDatesAsync(section, catalog);
            }
            else if (catalog.Versions.Count > 0 && section.Kind == MinecraftLoaderKind.Quilt)
            {
                _ = FillQuiltDatesAsync(section, catalog);
            }
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
            // 返回版本列表后, 不再更新已经离开的详情页.
        }
        catch (Exception error)
        {
            section.SetError();
            App.Logger.Error("Download", $"获取 {_version.Id} 的 {section.Kind} 版本列表失败.", error);
        }
    }

    private async Task FillForgeReleaseTimesAsync(LoaderSection section, MinecraftLoaderVersionCatalog catalog)
    {
        try
        {
            IReadOnlyDictionary<string, DateTimeOffset> dates = await _loaderClient.GetForgeReleaseTimesAsync(
                _version.Id,
                catalog.Versions.Select(version => version.Version).ToArray(),
                _pageCancellation.Token);
            if (_pageCancellation.IsCancellationRequested)
            {
                return;
            }

            section.ApplyReleaseTimes(dates);
            if (catalog.Source == MinecraftManifestSource.Official && dates.Count == 0)
            {
                _ = FillForgeDatesAsync(section);
            }
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
            // 页面已关闭, 不再补充日期.
        }
        catch (Exception error)
        {
            App.Logger.Warning("Download", $"无法补充 {_version.Id} 的 {section.Kind} 发布时间: {error.Message}");
            if (catalog.Source == MinecraftManifestSource.Official
                && !_pageCancellation.IsCancellationRequested)
            {
                _ = FillForgeDatesAsync(section);
            }
        }
    }

    private async Task FillFabricDatesAsync(LoaderSection section, MinecraftLoaderVersionCatalog catalog)
    {
        try
        {
            IReadOnlyDictionary<string, DateOnly> dates = await _loaderClient.GetFabricVersionDatesAsync(
                catalog.Versions.Select(version => version.Version).ToArray(), _pageCancellation.Token);
            if (!_pageCancellation.IsCancellationRequested)
            {
                section.ApplyModifiedDates(dates);
            }
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
            // 页面已关闭, 不再补充日期.
        }
        catch (Exception error)
        {
            App.Logger.Warning("Download", $"无法补充 {_version.Id} 的 Fabric 更新日期: {error.Message}");
        }
    }

    private async Task FillQuiltDatesAsync(LoaderSection section, MinecraftLoaderVersionCatalog catalog)
    {
        try
        {
            // 分批补充日期, 让已取得的结果先显示, 避免等待全部版本请求完成.
            foreach (MinecraftLoaderVersion[] batch in catalog.Versions.Chunk(24))
            {
                IReadOnlyDictionary<string, DateOnly> dates = await _loaderClient.GetQuiltVersionDatesAsync(
                    batch.Select(version => version.Version).ToArray(), _pageCancellation.Token);
                if (_pageCancellation.IsCancellationRequested)
                {
                    return;
                }

                section.ApplyModifiedDates(dates);
            }
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
            // 页面已关闭, 不再补充日期.
        }
        catch (Exception error)
        {
            App.Logger.Warning("Download", $"无法补充 {_version.Id} 的 Quilt 更新日期: {error.Message}");
        }
    }

    private async Task FillForgeDatesAsync(LoaderSection section)
    {
        try
        {
            IReadOnlyDictionary<string, DateOnly> dates = await _loaderClient.GetForgeVersionDatesAsync(
                _version.Id, _pageCancellation.Token);
            if (!_pageCancellation.IsCancellationRequested)
            {
                section.ApplyModifiedDates(dates);
            }
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
            // 页面已关闭, 不再补充日期.
        }
        catch (Exception error)
        {
            // 日期不是选择加载器版本的必要信息.
            App.Logger.Warning("Download", $"无法补充 {_version.Id} 的 Forge 版本日期: {error.Message}");
        }
    }

    private void LoaderContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LoaderSection section, Parent: StackPanel parent } content)
        {
            return;
        }

        ToggleButton header = parent.Children.OfType<ToggleButton>().First();
        _sectionControls[section] = (header, content);
        content.Height = section.IsExpanded ? double.NaN : 0;
    }

    private void LoaderHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: LoaderSection section, Tag: FrameworkElement content } header)
        {
            return;
        }

        if (!section.HasVersions)
        {
            header.IsChecked = false;
            if (section.HasError)
            {
                _ = LoadSectionAsync(section);
            }

            return;
        }

        section.IsExpanded = header.IsChecked == true;
        AnimateContent(content, section.IsExpanded);
    }

    private void LoaderChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelections || sender is not ListBox { Tag: LoaderSection section, SelectedItem: LoaderChoice choice })
        {
            return;
        }

        MinecraftLoaderVersion? selected = choice.Version;
        if (selected is not null)
        {
            foreach (LoaderSection other in LoaderSections)
            {
                if (other == section || other.SelectedVersion is not { } previous)
                {
                    continue;
                }

                if (!MinecraftLoaderCompatibility.CanCombine(_version.Id, section.Kind, selected, other.Kind, previous))
                {
                    other.Select(null);
                    App.Logger.Information("Download", $"选择 {section.Kind} 后清除不兼容的 {other.Kind} 版本.");
                }
            }
        }

        section.Select(selected);
        RefreshCompatibility();
        App.Logger.Information("Download", $"{section.Kind} 版本选择: {choice.Label}.");
        if (_sectionControls.TryGetValue(section, out var controls))
        {
            controls.Header.IsChecked = false;
            section.IsExpanded = false;
            AnimateContent(controls.Content, false);
        }
    }

    private void RefreshCompatibility()
    {
        _updatingSelections = true;
        try
        {
            MinecraftLoaderVersion? selectedForge = LoaderSections.First(section => section.Kind == MinecraftLoaderKind.Forge).SelectedVersion;
            MinecraftLoaderVersion? selectedOptiFine = LoaderSections.First(section => section.Kind == MinecraftLoaderKind.OptiFine).SelectedVersion;
            foreach (LoaderSection section in LoaderSections)
            {
                section.ShowCompatibleVersions(version => section.Kind switch
                {
                    MinecraftLoaderKind.Forge when selectedOptiFine is not null =>
                        MinecraftLoaderCompatibility.CanCombine(_version.Id, section.Kind, version, MinecraftLoaderKind.OptiFine, selectedOptiFine),
                    MinecraftLoaderKind.OptiFine when selectedForge is not null =>
                        MinecraftLoaderCompatibility.CanCombine(_version.Id, section.Kind, version, MinecraftLoaderKind.Forge, selectedForge),
                    _ => true
                });
                if (_sectionControls.TryGetValue(section, out var controls)
                    && controls.Content is Border { Child: ListBox list })
                {
                    list.SelectedItem = section.Choices.FirstOrDefault(choice => !choice.IsNone
                                                                                 && choice.Version?.Version == section.SelectedVersion?.Version);
                    if (!section.HasVersions && section.IsExpanded)
                    {
                        controls.Header.IsChecked = false;
                        section.IsExpanded = false;
                        AnimateContent(controls.Content, false);
                    }
                }
            }
        }
        finally
        {
            _updatingSelections = false;
        }
    }

    private void AnimateContent(FrameworkElement content, bool expanding)
    {
        double currentHeight = content.ActualHeight;
        content.BeginAnimation(HeightProperty, null);
        content.Height = double.NaN;
        content.Measure(new Size(Math.Max(content.ActualWidth, 100), double.PositiveInfinity));
        double targetHeight = expanding ? content.DesiredSize.Height : 0;
        content.Height = currentHeight;

        int version = _animationVersions.GetValueOrDefault(content) + 1;
        _animationVersions[content] = version;
        DoubleAnimation animation = new(currentHeight, targetHeight, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (_animationVersions.GetValueOrDefault(content) == version)
            {
                content.Height = expanding ? double.NaN : 0;
            }
        };
        content.BeginAnimation(HeightProperty, animation);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        DetachWindow();
        _pageCancellation.Cancel();
        BackRequested?.Invoke();
    }

    private void OwnerWindow_Closed(object? sender, EventArgs e)
    {
        DetachWindow();
        _pageCancellation.Cancel();
    }

    private void DetachWindow()
    {
        LocalizationManager.Changed -= LocalizationChanged;
        if (_ownerWindow is not null)
        {
            _ownerWindow.Closed -= OwnerWindow_Closed;
            _ownerWindow = null;
        }
    }

    private void LocalizationChanged(object? sender, EventArgs e)
    {
        foreach (LoaderSection section in LoaderSections) section.RefreshLocalization();
        RefreshCompatibility();
    }

    private void StartDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Download", $"点击开始下载: {_version.Id}, 自定义名称: {VersionNameBox.Text}. 下载功能尚未实现.");
        LauncherToasts.Information(LocalizationManager.Get("versionDetails.downloadUnavailable"));
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ELCL/0.0.1");
        return client;
    }

    /// <summary>一个加载器下拉分组的状态和可选版本.</summary>
    public sealed class LoaderSection : INotifyPropertyChanged
    {
        private string _statusText = LocalizationManager.Get("versionDetails.waiting");
        private bool _hasError;
        private bool _hasVersions;
        private bool _isExpanded;
        private IReadOnlyList<LoaderChoice> _choices = [];
        private IReadOnlyList<MinecraftLoaderVersion> _allVersions = [];

        internal LoaderSection(MinecraftLoaderKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        /// <summary>加载状态或可选版本变化时通知页面.</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>加载器类别.</summary>
        public MinecraftLoaderKind Kind { get; }

        /// <summary>显示名称.</summary>
        public string Name { get; }

        /// <summary>当前获取或选择状态.</summary>
        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value;
                Changed(nameof(StatusText));
            }
        }

        /// <summary>获取失败时用于突出状态文字.</summary>
        public bool HasError
        {
            get => _hasError;
            private set
            {
                _hasError = value;
                Changed(nameof(HasError));
            }
        }

        /// <summary>是否存在可展开的版本列表.</summary>
        public bool HasVersions
        {
            get => _hasVersions;
            private set
            {
                _hasVersions = value;
                Changed(nameof(HasVersions));
            }
        }

        /// <summary>下拉列表当前是否展开.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            internal set
            {
                _isExpanded = value;
                Changed(nameof(IsExpanded));
            }
        }

        /// <summary>包含清除选项的可选版本列表.</summary>
        public IReadOnlyList<LoaderChoice> Choices
        {
            get => _choices;
            private set
            {
                _choices = value;
                Changed(nameof(Choices));
            }
        }

        internal bool IsLoading { get; private set; }

        internal bool HasLoaded { get; private set; }

        internal MinecraftLoaderVersion? SelectedVersion { get; private set; }

        internal void SetLoading()
        {
            IsLoading = true;
            HasLoaded = false;
            HasError = false;
            StatusText = LocalizationManager.Get("versionDetails.loading");
        }

        internal void SetVersions(IReadOnlyList<MinecraftLoaderVersion> versions)
        {
            IsLoading = false;
            HasLoaded = true;
            _allVersions = versions;
            ShowCompatibleVersions(_ => true);
        }

        internal void ShowCompatibleVersions(Func<MinecraftLoaderVersion, bool> isCompatible)
        {
            if (!HasLoaded || IsLoading || HasError)
            {
                return;
            }

            MinecraftLoaderVersion[] visible = _allVersions.Where(isCompatible).ToArray();
            HasVersions = visible.Length > 0;
            if (Choices.Count != visible.Length + (HasVersions ? 1 : 0)
                || !Choices.Skip(1).Select(choice => choice.Label).SequenceEqual(visible.Select(version => version.Version)))
            {
                Choices = HasVersions ? [new LoaderChoice(null, LocalizationManager.Get("common.none"), LocalizationManager.Get("versionDetails.doNotAddLoader")), .. visible.Select(version => new LoaderChoice(version, version.Version, DescribeVersion(version)))] : [];
            }

            StatusText = SelectedVersion is not null ? string.Format(LocalizationManager.Get("loader.selected"), SelectedVersion.Version) : _allVersions.Count == 0 ? LocalizationManager.Get("common.none") : HasVersions ? LocalizationManager.Get("versionDetails.availableToAdd") : LocalizationManager.Get("versionDetails.noCompatibleVersions");
        }

        internal void SetError()
        {
            IsLoading = false;
            HasError = true;
            StatusText = LocalizationManager.Get("versionDetails.loadFailedRetry");
        }

        internal void RefreshLocalization()
        {
            if (IsLoading) StatusText = LocalizationManager.Get("versionDetails.loading");
            else if (HasError) StatusText = LocalizationManager.Get("versionDetails.loadFailedRetry");
            else if (HasLoaded) Choices = [];
            else StatusText = LocalizationManager.Get("versionDetails.waiting");
        }

        internal void Select(MinecraftLoaderVersion? version)
        {
            SelectedVersion = version;
            StatusText = version is null ? LocalizationManager.Get("versionDetails.availableToAdd") : string.Format(LocalizationManager.Get("loader.selected"), version.Version);
        }

        internal void ApplyModifiedDates(IReadOnlyDictionary<string, DateOnly> dates)
        {
            _allVersions = _allVersions.Select(version => dates.TryGetValue(version.Version, out DateOnly modified) ? version with { LastModifiedDate = modified } : version).ToArray();
            if (SelectedVersion is not null)
            {
                SelectedVersion = _allVersions.FirstOrDefault(version => version.Version == SelectedVersion.Version);
            }

            foreach (LoaderChoice choice in Choices)
            {
                if (dates.TryGetValue(choice.Label, out DateOnly modified))
                {
                    choice.SetDetail(string.Format(LocalizationManager.Get("loader.updatedAt"), modified));
                }
            }
        }

        internal void ApplyReleaseTimes(IReadOnlyDictionary<string, DateTimeOffset> dates)
        {
            _allVersions = _allVersions.Select(version => dates.TryGetValue(version.Version, out DateTimeOffset released)
                ? version with { ReleaseTime = released }
                : version).ToArray();
            if (SelectedVersion is not null)
            {
                SelectedVersion = _allVersions.FirstOrDefault(version => version.Version == SelectedVersion.Version);
            }

            foreach (LoaderChoice choice in Choices)
            {
                if (dates.ContainsKey(choice.Label))
                {
                    MinecraftLoaderVersion? version = _allVersions.FirstOrDefault(item => item.Version == choice.Label);
                    if (version is not null)
                    {
                        choice.SetDetail(DescribeVersion(version));
                    }
                }
            }
        }

        private string DescribeVersion(MinecraftLoaderVersion version)
        {
            string date;
            if (version.ReleaseTime is { } releaseTime)
            {
                date = string.Format(LocalizationManager.Get("loader.releasedAtTime"), releaseTime.ToLocalTime());
            }
            else if (version.ReleaseDate is { } releaseDate)
            {
                date = string.Format(LocalizationManager.Get("loader.releasedAt"), releaseDate);
            }
            else if (version.LastModifiedDate is { } modifiedDate)
            {
                date = string.Format(LocalizationManager.Get("loader.updatedAt"), modifiedDate);
            }
            else if (version.BuildDate is { } buildDate)
            {
                date = string.Format(LocalizationManager.Get("loader.builtAt"), buildDate);
            }
            else
            {
                date = LocalizationManager.Get("versionDetails.releaseDateUnavailable");
            }

            if (Kind != MinecraftLoaderKind.OptiFine)
            {
                return date;
            }

            return version.RequiredForgeVersion is { } requirement
                ? string.Format(LocalizationManager.Get("loader.forgeCompatible"), date, requirement.Length == 0 ? LocalizationManager.Get("versionDetails.anyVersion") : requirement)
                : string.Format(LocalizationManager.Get("loader.forgeIncompatible"), date);
        }

        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>下拉列表中的一个版本选项, 包括清除选项.</summary>
    public sealed class LoaderChoice : INotifyPropertyChanged
    {
        internal LoaderChoice(MinecraftLoaderVersion? version, string label, string detail)
        {
            Version = version;
            Label = label;
            Detail = detail;
        }

        /// <summary>补充日期时通知已经显示的列表项.</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>加载器版本号或清除选项.</summary>
        public string Label { get; }

        internal MinecraftLoaderVersion? Version { get; }

        /// <summary>日期或清除选项的说明.</summary>
        public string Detail { get; private set; }

        /// <summary>是否为预览版本.</summary>
        public bool IsPreview => Version?.IsPreview == true;

        /// <summary>是否为清除选择的“无”选项.</summary>
        public bool IsNone => Version is null;

        internal void SetDetail(string detail)
        {
            Detail = detail;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        }
    }
}
