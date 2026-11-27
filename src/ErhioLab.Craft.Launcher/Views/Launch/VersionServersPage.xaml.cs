using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher.Views.Launch;

public partial class VersionServersPage
{
    private const int MaxConcurrentQueries = 6;
    private readonly SemaphoreSlim _pingSlots = new(MaxConcurrentQueries);
    private LocalMinecraftVersion? _version;
    private CancellationTokenSource? _scope;
    private bool _saving;

    internal event Func<MinecraftServerAddress, Task>? JoinRequested;

    public VersionServersPage()
    {
        InitializeComponent();
        SelectionBar.AddAction("servers.join", () => WithSelected(JoinAsync));
        SelectionBar.AddAction("servers.refresh", () => WithSelected(RefreshRowAsync));
        SelectionBar.AddAction("servers.rename", () => WithSelected(RenameAsync));
        SelectionBar.DeleteRequested += () => WithSelected(DeleteAsync);
        SelectionBar.DeselectRequested += () => ServerList.SelectedItem = null;
    }

    internal void SetVersion(LocalMinecraftVersion version)
    {
        StopQueries();
        CloseAdd();
        _version = version;
        ServerList.ItemsSource = null;
        if (IsLoaded) _ = RefreshAsync(null);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync(null);

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        StopQueries();
        CloseAdd();
    }

    private void StopQueries()
    {
        _scope?.Cancel();
        _scope?.Dispose();
        _scope = null;
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (!IsLoaded || !RefreshAllButton.IsEnabled) return;
        RefreshAllButton.IsEnabled = false;
        try
        {
            await RefreshAsync((ServerList.SelectedItem as ServerRow)?.Server.Index, notify: true);
        }
        finally
        {
            RefreshAllButton.IsEnabled = true;
        }
    }

    private async Task RefreshAsync(int? selectIndex, bool notify = false)
    {
        if (_version is not { } version || !IsLoaded) return;
        StopQueries();
        _scope = new CancellationTokenSource();
        var token = _scope.Token;
        ServerList.ItemsSource = null;
        SetStatus("reading");
        if (notify) LauncherToasts.Information(L("refreshAllStarted"));
        ServerRow[] rows;
        try
        {
            rows = await Task.Run(() => ReadRowsAsync(version.FolderPath, token), token);
            token.ThrowIfCancellationRequested();
            ServerList.ItemsSource = rows;
            ServerList.SelectedItem = rows.FirstOrDefault(row => row.Server.Index == selectIndex);
            if (ServerList.SelectedItem is { } selected) ServerList.ScrollIntoView(selected);
            SetStatus(rows.Length == 0 ? "empty" : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error) when (IsListReadError(error))
        {
            if (token.IsCancellationRequested) return;
            SetStatus("readFailed");
            if (notify) LauncherToasts.Error(L("readFailed"));
            return;
        }

        await ProbeRowsAsync(rows, ServerList.SelectedItem as ServerRow, token);
        if (notify && !token.IsCancellationRequested) LauncherToasts.Information(L("refreshAllFinished"));
    }

    private static async Task<ServerRow[]> ReadRowsAsync(string folder, CancellationToken token)
    {
        try
        {
            App.Logger.Information("Servers", $"读取版本服务器列表: {folder}.");
            var entries = await MinecraftServerList.ReadAsync(folder, token).ConfigureAwait(false);
            List<ServerRow> rows = [];
            foreach (var server in entries)
            {
                token.ThrowIfCancellationRequested();
                if (!server.IsHidden) rows.Add(new ServerRow(server, ModIconDecoder.Decode(server.Icon)));
            }

            App.Logger.Information("Servers", $"已读取 {rows.Count} 台已保存服务器.");
            return [.. rows];
        }
        catch (Exception error) when (!token.IsCancellationRequested && IsListReadError(error))
        {
            App.Logger.Error("Servers", $"读取服务器列表失败: {folder}.", error);
            throw;
        }
    }

    private static bool IsListReadError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException;

    private async Task ProbeRowsAsync(ServerRow[] rows, ServerRow? selectedRow, CancellationToken token)
    {
        var nextIndex = 0;
        // 工作循环在界面线程领取行, 查询在后台执行. 每个循环查完一行才领取下一行.
        var workers = new Task[Math.Min(rows.Length, MaxConcurrentQueries)];
        for (var i = 0; i < workers.Length; i++) workers[i] = RunWorkerAsync(i == 0 ? selectedRow : null);
        await Task.WhenAll(workers);
        return;

        async Task RunWorkerAsync(ServerRow? first)
        {
            if (first is { HasQueried: false }) await ProbeAsync(first, notify: false, token);
            while (!token.IsCancellationRequested && nextIndex < rows.Length)
            {
                var row = rows[nextIndex++];
                if (row != selectedRow && !row.HasQueried) await ProbeAsync(row, notify: false, token);
            }
        }
    }

    private void SetStatus(string? key)
    {
        ListStatus.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
        if (key is not null) ListStatus.SetResourceReference(TextBlock.TextProperty, "I18n.servers." + key);
    }

    private void WithSelected(Func<ServerRow, Task> action)
    {
        if (ServerList.SelectedItem is ServerRow row) _ = action(row);
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectionBar is null) return;
        var selected = ServerList.SelectedItem as ServerRow;
        SelectionBar.UpdateSelection(selected is null ? 0 : 1, selected is null ? "" : string.Format(L("selected"), selected.Name));
    }

    private async void ServerList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject origin) return;
        for (DependencyObject? element = origin; element is Visual; element = VisualTreeHelper.GetParent(element))
        {
            if (element is Button) return;
            if (element is not ListBoxItem { DataContext: ServerRow row }) continue;
            e.Handled = true;
            await JoinAsync(row);
            break;
        }
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServerRow row }) await JoinAsync(row);
    }

    private async Task JoinAsync(ServerRow row)
    {
        if (_saving) return;
        if (!MinecraftServerAddress.TryParse(row.Server.Address, out var address))
        {
            LauncherToasts.Error(L("invalidAddress"));
            return;
        }

        try
        {
            if (JoinRequested is { } join) await join(address);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Error("Servers", $"无法启动客户端以进入服务器: {address}.", error);
            LauncherToasts.Error(L("joinUnavailable"));
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServerRow row }) await RefreshRowAsync(row);
    }

    private Task RefreshRowAsync(ServerRow row) => _scope is { } scope && ServerList.Items.Contains(row)
        ? ProbeAsync(row, notify: true, scope.Token)
        : Task.CompletedTask;

    private async Task ProbeAsync(ServerRow row, bool notify, CancellationToken token)
    {
        if (token.IsCancellationRequested || row.IsQuerying) return;
        row.BeginProbe();
        if (notify) LauncherToasts.Information(string.Format(L("queryStarted"), row.Name));
        try
        {
            var result = await Task.Run(() => QueryStatusAsync(row.Server.Address, token), token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                row.EndProbe(result.Status, result.Icon, result.State);
                if (!notify) return;
                if (result.State is null) LauncherToasts.Success(string.Format(L("queryFinished"), row.Name));
                else if (result.State == "invalidAddress") LauncherToasts.Error(L("invalidAddress"));
                else LauncherToasts.Error(string.Format(L(result.State == "offline" ? "queryOffline" : "queryFailed"), row.Name));
            }, DispatcherPriority.Background, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task<ProbeResult> QueryStatusAsync(string savedAddress, CancellationToken token)
    {
        if (!MinecraftServerAddress.TryParse(savedAddress, out var address)) return new ProbeResult(null, null, "invalidAddress");

        await _pingSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            App.Logger.Information("Servers", $"查询服务器状态: {address}.");
            var status = await QueryWithRetryAsync(address, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var icon = ModIconDecoder.Decode(status.Icon);
            App.Logger.Information("Servers", $"服务器状态已更新: {address}.");
            return new ProbeResult(status, icon, null);
        }
        catch (Exception error) when (error is IOException or SocketException or InvalidDataException or JsonException or TimeoutException)
        {
            token.ThrowIfCancellationRequested();
            App.Logger.Error("Servers", $"无法查询服务器状态: {address}.", error);
            return new ProbeResult(null, null, error is InvalidDataException or JsonException ? "unreachable" : "offline");
        }
        finally
        {
            _pingSlots.Release();
        }
    }

    private readonly record struct ProbeResult(MinecraftServerStatus? Status, ImageSource? Icon, string? State);

    private static async Task<MinecraftServerStatus> QueryWithRetryAsync(MinecraftServerAddress address, CancellationToken token)
    {
        try
        {
            return await MinecraftServerStatusClient.PingAsync(address, WindowsMinecraftSrv.ResolveAsync, cancellationToken: token);
        }
        catch (Exception error) when (!token.IsCancellationRequested && CanRetry(error))
        {
            App.Logger.Warning("Servers", $"服务器状态查询短暂失败, 将重试一次: {address}. {error.Message}");
            await Task.Delay(TimeSpan.FromMilliseconds(600), token);
            return await MinecraftServerStatusClient.PingAsync(address, WindowsMinecraftSrv.ResolveAsync, cancellationToken: token);
        }
    }

    private static bool CanRetry(Exception error) => error switch
    {
        EndOfStreamException or TimeoutException => true,
        SocketException socket => socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted
            or SocketError.NetworkReset or SocketError.TimedOut or SocketError.TryAgain,
        IOException { InnerException: SocketException socket } => CanRetry(socket),
        _ => false
    };

    private void OpenAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_version is { } version && CanEdit(version))
        {
            AddForm.IsExpanded = !AddForm.IsExpanded;
            if (AddForm.IsExpanded) NameBox.Focus();
        }
    }

    private void CancelAdd_Click(object sender, RoutedEventArgs e) => CloseAdd();

    private void CloseAdd()
    {
        AddForm.IsExpanded = false;
        NameBox.Clear();
        AddressBox.Clear();
    }

    private async void SaveAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_version is not { } version) return;
        string name = NameBox.Text.Trim(), address = AddressBox.Text.Trim();
        if (ValidateName(name) is { } invalid)
        {
            LauncherToasts.Error(invalid);
            return;
        }

        if (!MinecraftServerAddress.TryParse(address, out _))
        {
            LauncherToasts.Error(L("invalidAddress"));
            return;
        }

        await EditAsync(version, "add", async () => (await MinecraftServerList.AddAsync(version.FolderPath, name, address)).Index,
            "added", onSaved: CloseAdd);
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServerRow row }) await RenameAsync(row);
    }

    private async Task RenameAsync(ServerRow row)
    {
        if (_version is not { } version || !CanEdit(version)) return;
        var name = await LauncherDialogs.InputAsync(L("rename"), L("renamePrompt"), row.Name, ValidateName);
        if (name is null || name.Trim() == row.Server.Name || !IsLoaded) return;
        await EditAsync(version, "rename", async () => (await MinecraftServerList.RenameAsync(version.FolderPath, row.Server, name)).Index, "renamed");
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServerRow row }) await DeleteAsync(row);
    }

    private async Task DeleteAsync(ServerRow row)
    {
        if (_version is not { } version || !CanEdit(version)) return;
        var delete = LocalizationManager.Get("common.delete");
        if (!await LauncherDialogs.ConfirmAsync(delete, string.Format(L("deleteConfirm"), row.Name), confirmText: delete) || !IsLoaded) return;
        await EditAsync(version, "delete", async () =>
        {
            await MinecraftServerList.RemoveAsync(version.FolderPath, row.Server);
            return null;
        }, "removed");
    }

    private async Task EditAsync(LocalMinecraftVersion version, string operation, Func<Task<int?>> action, string done, Action? onSaved = null)
    {
        if (!ReferenceEquals(version, _version) || !CanEdit(version)) return;
        _saving = true;
        int? index;
        try
        {
            App.Logger.Information("Servers", $"开始修改服务器列表: {operation}, {version.FolderPath}.");
            index = await action();
            App.Logger.Information("Servers", $"服务器列表修改完成: {operation}, {version.FolderPath}.");
            LauncherToasts.Success(L(done));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            App.Logger.Error("Servers", $"无法修改服务器列表: {operation}, {version.FolderPath}.", error);
            LauncherToasts.Error(L("editFailed"));
            return;
        }
        finally
        {
            _saving = false;
        }

        if (IsLoaded && ReferenceEquals(version, _version))
        {
            onSaved?.Invoke();
            await RefreshAsync(index);
        }
    }

    private bool CanEdit(LocalMinecraftVersion version)
    {
        if (_saving) return false;
        if (!App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, version.FolderPath, StringComparison.OrdinalIgnoreCase))) return true;
        LauncherToasts.Information(L("versionBusy"));
        return false;
    }

    private static string? ValidateName(string name)
    {
        var text = name.Trim();
        return text.Length is >= 1 and <= 128 && !text.Any(char.IsControl) ? null : L("invalidName");
    }

    private static string L(string key) => LocalizationManager.Get("servers." + key);

    internal sealed class ServerRow(MinecraftSavedServer server, ImageSource? savedIcon) : INotifyPropertyChanged
    {
        private readonly ImageSource? _savedIcon = savedIcon;
        private MinecraftServerStatus? _status;
        private string? _state = "waiting";
        public event PropertyChangedEventHandler? PropertyChanged;
        internal MinecraftSavedServer Server { get; } = server;
        internal bool HasQueried { get; private set; }
        internal bool IsQuerying { get; private set; }
        public ImageSource? Icon { get; private set; } = savedIcon;
        public bool HasIcon => Icon is not null;
        public string Name => string.IsNullOrWhiteSpace(Server.Name) ? Server.Address : Server.Name;
        public IReadOnlyList<MinecraftTextSpan> Description => _status?.Description ?? [];
        public string DescriptionText => string.Concat(Description.Select(span => span.Text));
        public Visibility DescriptionVisibility => Description.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        public string Summary => _state is { } state
            ? Server.Address + " · " + L(state)
            : string.Join(" · ",
                new[]
                {
                    Server.Address,
                    _status?.Version,
                    _status?.OnlinePlayers is { } online
                        ? _status.MaximumPlayers is { } max
                            ? string.Format(CultureInfo.CurrentCulture, L("playerCapacity"), online, max)
                            : string.Format(CultureInfo.CurrentCulture, L("onlinePlayers"), online)
                        : null,
                    _status?.Latency is { } delay ? string.Format(CultureInfo.CurrentCulture, L("latency"), delay.TotalMilliseconds) : L("latencyUnavailable")
                }.Where(text => !string.IsNullOrWhiteSpace(text)));

        internal void BeginProbe()
        {
            IsQuerying = true;
            _state = "querying";
            OnPropertyChanged(nameof(Summary));
        }

        internal void EndProbe(MinecraftServerStatus? status, ImageSource? icon, string? state)
        {
            var previousDescription = Description;
            var previousIcon = Icon;
            var hadIcon = HasIcon;
            var previousDescriptionVisibility = DescriptionVisibility;
            HasQueried = true;
            IsQuerying = false;
            _status = status;
            _state = state;
            Icon = icon ?? _savedIcon;
            OnPropertyChanged(nameof(Summary));
            if (Icon != previousIcon) OnPropertyChanged(nameof(Icon));
            if (hadIcon != HasIcon) OnPropertyChanged(nameof(HasIcon));
            if (ReferenceEquals(previousDescription, Description)) return;
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(DescriptionText));
            if (previousDescriptionVisibility != DescriptionVisibility) OnPropertyChanged(nameof(DescriptionVisibility));
        }

        private void OnPropertyChanged(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
