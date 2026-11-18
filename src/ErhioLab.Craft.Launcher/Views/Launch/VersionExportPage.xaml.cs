using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Modpacks;
using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Tasks;
using ErhioLab.Craft.Launcher.Versions;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Launch;

public partial class VersionExportPage
{
    private LocalMinecraftVersion? _version;
    private string _gameDirectory = "";
    private ModpackExportScan? _scan;
    private IReadOnlyList<ModpackExportNode> _nodes = [];
    private CancellationTokenSource? _scanCancellation;
    private bool _scanning;
    private int _generation;

    public VersionExportPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_scan is null && !_scanning && _version is not null) await ScanAsync();
        };
        Unloaded += (_, _) => _scanCancellation?.Cancel();
        LocalizationManager.Changed += (_, _) =>
        {
            if (_scan is null) return;
            var selected = Selected();
            _nodes = ModpackExportNode.Create(_scan, RefreshSelection);
            foreach (var node in _nodes) node.ApplySelection(selected);
            CategoryList.ItemsSource = _nodes;
            RefreshSearch();
            RefreshSelection();
        };
    }

    internal void SetContext(string gameDirectory, LocalMinecraftVersion version)
    {
        _scanCancellation?.Cancel();
        _generation++;
        _gameDirectory = gameDirectory;
        _version = version;
        _scan = null;
        _nodes = [];
        _scanning = false;
        NameBox.Text = version.Id;
        VersionBox.Text = "1.0.0";
        IncludeLauncherBox.IsChecked = BundleBox.IsChecked = OnlyModrinthBox.IsChecked = false;
        FilesToggle.IsChecked = AdvancedToggle.IsChecked = false;
        SearchBox.Text = "";
        CategoryList.ItemsSource = _nodes;
        FileTree.ItemsSource = _nodes;
        GameText.Text = version.GameVersion ?? version.Id;
        ScanText.Text = version.IsValid ? "" : L("packExport.scanFailed");
        ScanText.Visibility = version.IsValid ? Visibility.Collapsed : Visibility.Visible;
        RefreshSelection();
    }

    internal Task RefreshAsync() => ScanAsync();

    private async Task ScanAsync()
    {
        if (_version is not { IsValid: true } version || _scanning) return;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        var cancellation = _scanCancellation = new CancellationTokenSource();
        int generation = _generation;
        var previous = _scan is null ? null : Selected();
        _scanning = true;
        StartButton.IsEnabled = false;
        ScanText.Text = L("packExport.scanning");
        ScanText.Visibility = Visibility.Visible;
        App.Logger.Information("Modpacks", $"开始扫描导出内容: {version.FolderPath}.");
        try
        {
            var scan = await ModpackExporter.ScanAsync(_gameDirectory, version, cancellation.Token);
            if (generation != _generation) return;
            _scan = scan;
            _nodes = ModpackExportNode.Create(scan, RefreshSelection);
            if (previous is not null)
                foreach (var node in _nodes)
                    node.ApplySelection(previous);
            CategoryList.ItemsSource = _nodes;
            GameText.Text = string.Join(" · ", scan.Dependencies.Select(pair => (pair.Key switch
            {
                "minecraft" => "Minecraft", "forge" => "Forge", "neoforge" => "NeoForge", "fabric-loader" => "Fabric",
                "quilt-loader" => "Quilt", "liteloader" => "LiteLoader", "optifine" => "OptiFine", _ => pair.Key
            }) + " " + pair.Value));
            ScanText.Text = "";
            ScanText.Visibility = Visibility.Collapsed;
            RefreshSearch();
            App.Logger.Information("Modpacks", $"导出扫描完成: {scan.Files.Count} 个文件, 游戏={scan.GameVersion}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _generation)
            {
                ScanText.Text = "";
                ScanText.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            if (generation != _generation) return;
            _scan = null;
            ScanText.Text = L("packExport.scanFailed");
            ScanText.Visibility = Visibility.Visible;
            App.Logger.Error("Modpacks", "扫描整合包导出内容失败.", error);
            LauncherToasts.Error(string.Format(L("packExport.failed"), error.Message));
        }
        finally
        {
            if (generation == _generation)
            {
                _scanning = false;
                RefreshSelection();
            }
        }
    }

    private HashSet<string> Selected() => _nodes.SelectMany(node => node.SelectedPaths()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void RefreshSelection()
    {
        if (StartButton is null) return;
        var selected = Selected();
        long bytes = _scan?.Files.Where(file => selected.Contains(file.Path)).Sum(file => file.Size) ?? 0;
        SelectionText.Text = string.Format(L("packExport.selection"), selected.Count, DataSizeFormatter.Format(bytes, App.Settings.MemoryUnit, "0.##", minimumPower: 0));
        StartButton.IsEnabled = _scan is not null && !_scanning && !string.IsNullOrWhiteSpace(NameBox.Text) && !string.IsNullOrWhiteSpace(VersionBox.Text);
    }

    private void Identity_Changed(object sender, TextChangedEventArgs e) => RefreshSelection();

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (FileTree is not null) RefreshSearch();
    }

    private void RefreshSearch()
    {
        string query = SearchBox.Text.Trim();
        foreach (var node in _nodes) node.Filter(query);
        FileTree.ItemsSource = _nodes.Where(node => node.Matches).ToArray();
    }

    private void Bundle_Changed(object sender, RoutedEventArgs e)
    {
        if (OnlyModrinthBox is not null) OnlyModrinthBox.IsEnabled = BundleBox.IsChecked != true;
    }

    private ModpackExportProfile Profile() => new(1, NameBox.Text.Trim(), VersionBox.Text.Trim(), Selected().ToArray(),
        IncludeLauncherBox.IsChecked == true, BundleBox.IsChecked == true, OnlyModrinthBox.IsChecked == true);

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null || _scanning)
        {
            LauncherToasts.Information(L("packExport.scanFirst"));
            return;
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(VersionBox.Text))
        {
            LauncherToasts.Error(L("packExport.invalidIdentity"));
            return;
        }

        var dialog = new SaveFileDialog { Title = L("packExport.saveProfile"), Filter = L("settings.jsonFileFilter"), DefaultExt = ".json", FileName = "export-config.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        App.Logger.Information("Modpacks", $"开始保存导出配置: {dialog.FileName}.");
        try
        {
            var profile = Profile();
            if (JsonSerializer.SerializeToUtf8Bytes(profile, new JsonSerializerOptions { WriteIndented = true }).Length > 8 * 1024 * 1024)
                throw new InvalidDataException(L("packExport.profileTooLarge"));
            new JsonFileStore<ModpackExportProfile>(dialog.FileName).Save(profile);
            App.Logger.Information("Modpacks", $"导出配置已保存: {dialog.FileName}.");
            LauncherToasts.Success(L("packExport.profileSaved"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ReportError(error);
        }
    }

    private void ReadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null || _scanning)
        {
            LauncherToasts.Information(L("packExport.scanFirst"));
            return;
        }

        var dialog = new OpenFileDialog { Title = L("packExport.readProfile"), Filter = L("settings.jsonFileFilter"), CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        App.Logger.Information("Modpacks", $"开始读取导出配置: {dialog.FileName}.");
        try
        {
            if (new FileInfo(dialog.FileName).Length > 8 * 1024 * 1024) throw new InvalidDataException(L("packExport.invalidProfile"));
            var profile = JsonSerializer.Deserialize<ModpackExportProfile>(File.ReadAllText(dialog.FileName));
            int missing = ApplyProfile(profile);
            App.Logger.Information("Modpacks", $"已读取导出配置: {dialog.FileName}, 未匹配路径={missing}.");
            LauncherToasts.Success(string.Format(L("packExport.profileLoaded"), missing));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            ReportError(error);
        }
    }

    private int ApplyProfile(ModpackExportProfile? profile)
    {
        if (_scan is null || profile is not { FormatVersion: 1, SelectedPaths.Length: <= 99000 } || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 128
            || string.IsNullOrWhiteSpace(profile.Version) || profile.Version.Length > 64 || profile.Name.Any(char.IsControl) || profile.Version.Any(char.IsControl)
            || profile.SelectedPaths.Any(path => string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(part => part is "." or "..")))
            throw new InvalidDataException(L("packExport.invalidProfile"));
        var selected = profile.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int missing = selected.Except(_scan.Files.Where(file => file.CanSelect).Select(file => file.Path), StringComparer.OrdinalIgnoreCase).Count();
        NameBox.Text = profile.Name;
        VersionBox.Text = profile.Version;
        IncludeLauncherBox.IsChecked = profile.IncludeLauncher;
        BundleBox.IsChecked = profile.BundleResources;
        OnlyModrinthBox.IsChecked = profile.OnlyModrinth;
        foreach (var node in _nodes) node.ApplySelection(selected);
        RefreshSelection();
        return missing;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null || _scanning || !StartButton.IsEnabled) return;
        var profile = Profile();
        var dialog = new SaveFileDialog
        {
            Title = L("packExport.start"), Filter = L(profile.IncludeLauncher ? "packExport.zipFilter" : "packExport.packFilter"),
            DefaultExt = profile.IncludeLauncher ? ".zip" : ".mrpack", FileName = SafeName(profile.Name + "-" + profile.Version) + (profile.IncludeLauncher ? ".zip" : ".mrpack")
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            ModpackExportTasks.Start(_scan, new(profile.Name, profile.Version, profile.SelectedPaths, profile.BundleResources, profile.OnlyModrinth, true), profile.IncludeLauncher, dialog.FileName);
            LauncherToasts.Information(L("packExport.queued"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ReportError(error);
        }
    }

    internal static string SafeName(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.');

    private static void ReportError(Exception error)
    {
        App.Logger.Error("Modpacks", "整合包导出操作失败.", error);
        LauncherToasts.Error(string.Format(L("packExport.failed"), error.Message));
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
