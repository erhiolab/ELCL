using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Tasks;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Download;

public partial class JavaDownloadPage
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly JavaDistributionClient _client = new(Http);
    private readonly JavaPlatform _platform = JavaPlatform.Current;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<JavaDownloadRow> _rows = [];
    private readonly JavaProviderOption[] _providers = Enum.GetValues<JavaDistribution>().Select(value => new JavaProviderOption(value)).ToArray();
    private CancellationTokenSource? _versionsCancellation;
    private bool _initialized;
    private bool _catalogLoading;
    private bool _binding;
    private bool _closeHooked;
    private bool _hasMore;
    private int _page;
    private string? _lastDirectory;

    public JavaDownloadPage()
    {
        InitializeComponent();
        _binding = true;
        SourcesBox.ItemsSource = _providers;
        SourcesBox.SelectedIndex = 0;
        VersionsList.ItemsSource = _rows;
        _binding = false;
        RefreshDeviceText();
        LocalizationManager.Changed += (_, _) =>
        {
            RefreshDeviceText();
            SourcesBox.Items.Refresh();
            MajorsBox.Items.Refresh();
            VersionsList.Items.Refresh();
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_closeHooked && Window.GetWindow(this) is { } window)
        {
            window.Closed += (_, _) =>
            {
                _lifetime.Cancel();
                _versionsCancellation?.Cancel();
            };
            _closeHooked = true;
        }

        if (_initialized) return;
        _initialized = true;
        await RefreshCatalogsAsync(false);
    }

    private void RefreshDeviceText() => DeviceText.Text = string.Format(L("javaDownload.device"), _platform.OperatingSystem, _platform.Architecture);
    internal void RefreshCatalogs() => _ = RefreshCatalogsAsync(true);
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCatalogs();

    private async Task RefreshCatalogsAsync(bool notify)
    {
        if (_catalogLoading || _lifetime.IsCancellationRequested) return;
        _catalogLoading = true;
        RefreshButton.IsEnabled = false;
        if (notify) LauncherToasts.Information(L("javaDownload.refreshing"));
        App.Logger.Information("Java", $"刷新 Java 发行目录: 设备={_platform}.");
        try
        {
            await Task.WhenAll(_providers.Select(async provider =>
            {
                try
                {
                    var majors = await _client.GetMajorVersionsAsync(provider.Distribution, _platform, _lifetime.Token);
                    provider.Majors = majors;
                    provider.Failed = false;
                    App.Logger.Information("Java", $"Java 主版本目录获取完成: {provider.Distribution}, {majors.Count} 项.");
                    if (ReferenceEquals(SourcesBox.SelectedItem, provider)) UpdateMajors();
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    provider.Failed = true;
                    App.Logger.Error("Java", $"Java 目录获取失败: {provider.Distribution}.", error);
                    if (ReferenceEquals(SourcesBox.SelectedItem, provider)) SourceStateText.SetResourceReference(TextBlock.TextProperty, "I18n.javaDownload.sourceFailed");
                }
            }));
            if (_lifetime.IsCancellationRequested) return;
            _binding = true;
            SourcesBox.Items.Refresh();
            bool changeSource = false;
            if (SourcesBox.SelectedItem is not JavaProviderOption { Majors.Count: > 0 })
            {
                SourcesBox.SelectedItem = _providers.FirstOrDefault(provider => provider.Majors.Count > 0) ?? _providers[0];
                changeSource = true;
            }

            _binding = false;
            if (changeSource) UpdateMajors();
            if (notify)
            {
                if (_providers.Any(provider => provider.Failed)) LauncherToasts.Information(L("javaDownload.partialFailure"));
                else LauncherToasts.Success(L("javaDownload.refreshed"));
            }
        }
        finally
        {
            _catalogLoading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_binding) UpdateMajors();
    }

    private void UpdateMajors()
    {
        if (SourcesBox.SelectedItem is not JavaProviderOption provider) return;
        int? previous = (MajorsBox.SelectedItem as JavaMajorOption)?.Major;
        _binding = true;
        var options = provider.Majors.Select(value => new JavaMajorOption(value)).ToArray();
        MajorsBox.ItemsSource = options;
        MajorsBox.SelectedItem = options.FirstOrDefault(option => option.Major == previous) ?? options.FirstOrDefault();
        SourceStateText.SetResourceReference(TextBlock.TextProperty, "I18n." + (provider.Failed ? "javaDownload.sourceFailed" : "javaDownload.selectionHint"));
        _binding = false;
        _ = LoadVersionsAsync(false);
    }

    private void Major_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_binding) _ = LoadVersionsAsync(false);
    }

    private async void More_Click(object sender, RoutedEventArgs e) => await LoadVersionsAsync(true);

    private async Task LoadVersionsAsync(bool append)
    {
        if (append && _versionsCancellation is not null) return;
        _versionsCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _versionsCancellation = cancellation;
        if (!append)
        {
            _rows.Clear();
            _page = 0;
        }

        MoreButton.IsEnabled = DownloadButton.IsEnabled = false;
        _hasMore = false;
        EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.javaDownload.loading");
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            if (SourcesBox.SelectedItem is not JavaProviderOption provider || MajorsBox.SelectedItem is not JavaMajorOption major)
            {
                EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.javaDownload.empty");
                return;
            }

            int requestedPage = append ? _page + 1 : 0;
            App.Logger.Information("Java", $"获取 Java 版本: {provider.Distribution}, Java {major.Major}, 页={requestedPage}, 设备={_platform}.");
            var result = await _client.GetVersionsAsync(provider.Distribution, major.Major, _platform, requestedPage, 10, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var keys = _rows.Select(row => row.Version.DownloadUri).ToHashSet();
            foreach (var version in result.Versions)
                if (keys.Add(version.DownloadUri))
                    _rows.Add(new JavaDownloadRow(version));
            _page = requestedPage;
            _hasMore = result.HasMore;
            if (VersionsList.SelectedItem is null) VersionsList.SelectedItem = _rows.FirstOrDefault();
            EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.javaDownload.empty");
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            App.Logger.Information("Java", $"Java 版本获取完成: {provider.Distribution}, Java {major.Major}, 本页={result.Versions.Count}, 还有下一页={result.HasMore}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (cancellation.IsCancellationRequested) return;
            EmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.javaDownload.listFailed");
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _hasMore = append;
            App.Logger.Error("Java", "获取 Java 下载版本失败.", error);
            LauncherToasts.Error(L("javaDownload.listFailed"));
        }
        finally
        {
            if (ReferenceEquals(_versionsCancellation, cancellation))
            {
                _versionsCancellation = null;
                MoreButton.IsEnabled = _hasMore;
                DownloadButton.IsEnabled = VersionsList.SelectedItem is JavaDownloadRow;
            }
        }
    }

    private void Version_Changed(object sender, SelectionChangedEventArgs e) => DownloadButton.IsEnabled = VersionsList.SelectedItem is JavaDownloadRow && _versionsCancellation is null;

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not JavaDownloadRow row) return;
        OpenFolderDialog dialog = new()
        {
            Title = L("javaDownload.chooseDirectory"),
            InitialDirectory = Directory.Exists(_lastDirectory) ? _lastDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _lastDirectory = dialog.FolderName;
        try
        {
            JavaInstallTasks.Start(row.Version, dialog.FolderName, new DownloadOptions(App.Settings.DownloadMaxConnections,
                (long)(App.Settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024)));
            LauncherToasts.Information(L("javaDownload.taskCreated"));
        }
        catch (Exception error)
        {
            App.Logger.Error("Java", $"无法创建 Java 安装任务: {row.Version.Name}.", error);
            LauncherToasts.Error(string.Format(L("javaDownload.createFailed"), error.Message));
        }
    }

    private static string L(string key) => LocalizationManager.Get(key);
}

internal sealed class JavaProviderOption(JavaDistribution distribution)
{
    internal JavaDistribution Distribution { get; } = distribution;
    internal IReadOnlyList<int> Majors { get; set; } = [];
    internal bool Failed { get; set; }
    public string Name => LocalizationManager.Get("javaDownload.provider." + Distribution);
}

internal sealed record JavaMajorOption(int Major)
{
    public string Name => "Java " + Major;
}

internal sealed record JavaDownloadRow(JavaDownloadVersion Version)
{
    public string Title => $"{Version.Version} · {Version.PackageType.ToString().ToUpperInvariant()}";

    public string Description => string.Join(" · ", new[]
    {
        Version.ReleasedAt?.ToLocalTime().ToString("d"),
        Version.Format == JavaDownloadFormat.Manifest || Version.ReleasedAt is null ? Version.Name : null,
        Version.Size is { } size && Version.Format != JavaDownloadFormat.Manifest ? DataSizeFormatter.Format(size, App.Settings.MemoryUnit, "0.##", 0) : null,
        Version.Platform.Architecture.ToString()
    }.Where(value => !string.IsNullOrEmpty(value)));

    public string FormatLabel => Version.Format == JavaDownloadFormat.Manifest ? LocalizationManager.Get("javaDownload.manifest")
        : Version.Format == JavaDownloadFormat.Zip ? "ZIP" : "tar.gz";
}
