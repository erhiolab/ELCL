using ErhioLab.Craft.Launcher.Localization;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.Versions;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Launch;

/// <summary>管理游戏文件夹并显示所选目录中的本地版本.</summary>
public partial class VersionSelectionView
{
    private readonly ObservableCollection<FolderRow> _folders = [];
    private int _scanVersion;
    private string _emptyVersionsKey = "versionSelector.readingVersions";

    /// <summary>用户选择版本后通知主窗口返回启动页.</summary>
    internal event Action<LocalMinecraftVersion>? VersionChosen;
    internal event Action<LocalMinecraftVersion>? VersionSettingsRequested;

    internal Task ReloadAsync()
    {
        ReloadFolders();
        return RefreshVersionsAsync();
    }

    /// <summary>扫描后自动选择版本时同步启动页, 不关闭版本选择页.</summary>
    internal event Action<LocalMinecraftVersion>? VersionSelectionChanged;

    /// <summary>当前所选版本被删除或所在文件夹被移除.</summary>
    internal event Action? VersionSelectionCleared;

    public VersionSelectionView()
    {
        InitializeComponent();
        FolderList.ItemsSource = _folders;
        LocalizationManager.Changed += (_, _) =>
        {
            ReloadFolders();
            FolderTitle.Text = _folders.FirstOrDefault(folder => folder.IsSelected)?.Name ?? LocalizationManager.Get("versionSelector.versionList");
            EmptyVersionsText.Text = LocalizationManager.Get(_emptyVersionsKey);
            VersionList.Items.Refresh();
            _ = Dispatcher.InvokeAsync(() => MoveSelectedFolderHighlight(false), DispatcherPriority.Loaded);
        };
    }

    private void View_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) ++_scanVersion;
    }

    internal void PrepareForOpen()
    {
        ++_scanVersion;
        VersionList.BeginAnimation(OpacityProperty, null);
        VersionList.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null);
        VersionList.Opacity = 0;
        VersionList.ItemsSource = null;
        SetEmptyVersionsText("versionSelector.readingVersions");
        EmptyVersionsText.Visibility = Visibility.Visible;
    }

    internal async Task LoadAfterEntranceAsync()
    {
        if (!IsVisible || !IsHitTestVisible) return;
        ReloadFolders();
        await Dispatcher.InvokeAsync(() => MoveSelectedFolderHighlight(false), DispatcherPriority.Loaded);
        if (!IsVisible || !IsHitTestVisible) return;
        await RefreshVersionsAsync();
    }

    private void ReloadFolders()
    {
        _folders.Clear();
        foreach (GameFolderSetting folder in App.Settings.GameFolders)
        {
            _folders.Add(new FolderRow(folder.Path, folder.NameKey is { } key ? LocalizationManager.Get(key) : folder.Name)
            {
                IsSelected = SamePath(folder.Path, App.Settings.SelectedGameFolderPath)
            });
        }

        if (_folders.Count > 0 && _folders.All(folder => !folder.IsSelected))
        {
            _folders[0].IsSelected = true;
        }
    }

    private async Task ReloadFoldersAnimatedAsync()
    {
        FolderList.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110)));
        await Task.Delay(110);
        FolderList.BeginAnimation(OpacityProperty, null);
        FolderList.Opacity = 0;
        SelectedFolderHighlight.BeginAnimation(OpacityProperty, null);
        SelectedFolderHighlight.Opacity = 0;
        ReloadFolders();
        FolderListContainer.UpdateLayout();
        MoveSelectedFolderHighlight(false);
        FolderList.Opacity = 1;
        FolderList.BeginAnimation(OpacityProperty, EaseOut(0, 1, 190));
    }

    private void MoveSelectedFolderHighlight(bool animate)
    {
        FolderRow? selected = _folders.FirstOrDefault(folder => folder.IsSelected);
        if (selected is null)
        {
            SelectedFolderHighlight.BeginAnimation(OpacityProperty, EaseOut(SelectedFolderHighlight.Opacity, 0, 140));
            return;
        }

        FolderListContainer.UpdateLayout();
        if (FolderList.ItemContainerGenerator.ContainerFromItem(selected) is not { } item ||
            FindChild<Border>(item, "FolderItem") is not { } rowBorder)
        {
            return;
        }

        double targetY = rowBorder.TransformToAncestor(FolderListContainer).Transform(new Point(0, 0)).Y;
        TranslateTransform offset = (TranslateTransform)SelectedFolderHighlight.RenderTransform;
        SelectedFolderHighlight.Height = rowBorder.ActualHeight;
        if (!animate || SelectedFolderHighlight.Opacity < 0.5)
        {
            offset.BeginAnimation(TranslateTransform.YProperty, null);
            offset.Y = targetY;
            SelectedFolderHighlight.Opacity = 1;
            SelectedFolderHighlight.BeginAnimation(OpacityProperty, EaseOut(0, 1, 190));
            return;
        }

        offset.BeginAnimation(TranslateTransform.YProperty, EaseOut(offset.Y, targetY, 250));
    }

    private async Task SelectFolderAsync(FolderRow row, bool refreshSelected = false)
    {
        if (row.IsSelected && SamePath(App.Settings.SelectedGameFolderPath, row.Path))
        {
            if (refreshSelected)
            {
                await RefreshVersionsAsync();
            }

            return;
        }

        if (!App.UpdateSettings(App.Settings with
            {
                SelectedGameFolderPath = row.Path,
                SelectedVersionPath = null,
                SelectedVersionId = null
            }))
        {
            return;
        }

        VersionSelectionCleared?.Invoke();

        foreach (FolderRow folder in _folders)
        {
            folder.IsSelected = ReferenceEquals(folder, row);
        }

        MoveSelectedFolderHighlight(true);

        await RefreshVersionsAsync();
    }

    private async Task RefreshVersionsAsync()
    {
        int version = ++_scanVersion;
        FolderRow? selected = _folders.FirstOrDefault(folder => folder.IsSelected);
        if (selected is not null && !SamePath(selected.Path, App.Settings.SelectedGameFolderPath))
        {
            if (!App.UpdateSettings(App.Settings with
                {
                    SelectedGameFolderPath = selected.Path,
                    SelectedVersionPath = null,
                    SelectedVersionId = null
                }))
            {
                return;
            }

            VersionSelectionCleared?.Invoke();
        }

        FolderTitle.Text = selected?.Name ?? LocalizationManager.Get("versionSelector.versionList");
        FolderPathText.Text = selected?.Path ?? string.Empty;
        FolderTitle.BeginAnimation(OpacityProperty, EaseOut(0.45, 1, 180));
        FolderPathText.BeginAnimation(OpacityProperty, EaseOut(0.45, 1, 180));
        bool hasPreviousRows = VersionList.Items.Count > 0;
        if (hasPreviousRows)
        {
            VersionList.BeginAnimation(OpacityProperty, EaseOut(VersionList.Opacity, 0, 130));
            await Task.Delay(130);
            if (version != _scanVersion)
            {
                return;
            }
        }

        VersionList.BeginAnimation(OpacityProperty, null);
        VersionList.Opacity = 0;
        VersionList.ItemsSource = null;
        SetEmptyVersionsText(selected is null ? "versionSelector.addFolderFirst" : "versionSelector.readingVersions");
        EmptyVersionsText.Visibility = Visibility.Visible;
        if (selected is null)
        {
            UpdateSelectedVersion(null);
            AnimateEmptyText();
            return;
        }

        try
        {
            App.Logger.Information("Versions", $"开始扫描游戏目录: {selected.Path}.");
            IReadOnlyList<LocalMinecraftVersion> found = await Task.Run(() => LocalMinecraftVersionScanner.Scan(selected.Path,
                (path, error) => App.Logger.Error("Versions", $"版本元数据读取失败, 保留错误条目: {path}.", error), includeInvalidVersions: true));
            if (version != _scanVersion)
            {
                return;
            }

            var descriptions = await Task.Run(() => found.ToDictionary(item => item.FolderPath, item => VersionSettingsStore.ReadDescription(item.FolderPath), StringComparer.OrdinalIgnoreCase));
            if (version != _scanVersion) return;
            string? rememberedPath = App.Settings.GameFolders.FirstOrDefault(folder => SamePath(folder.Path, selected.Path))?.LastSelectedVersionPath;
            string? preferredPath = rememberedPath ?? App.Settings.SelectedVersionPath;
            LocalMinecraftVersion[] validVersions = found.Where(item => item.IsValid).ToArray();
            LocalMinecraftVersion? selectedVersion = validVersions.FirstOrDefault(item => SamePath(item.FolderPath, preferredPath)) ?? validVersions.FirstOrDefault();
            UpdateSelectedVersion(selectedVersion);
            VersionList.ItemsSource = found.Select(item => new VersionRow(item, SamePath(item.FolderPath, App.Settings.SelectedVersionPath), descriptions[item.FolderPath])).ToArray();
            SetEmptyVersionsText("versionSelector.noVersions");
            EmptyVersionsText.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (found.Count > 0)
            {
                VersionList.Opacity = 1;
                VersionList.BeginAnimation(OpacityProperty, EaseOut(0, 1, 220));
                VersionList.RenderTransform.BeginAnimation(TranslateTransform.YProperty, EaseOut(9, 0, 220));
            }
            else
            {
                AnimateEmptyText();
            }

            App.Logger.Information("Versions", $"扫描游戏目录 {selected.Path}, 找到 {validVersions.Length} 个正常版本, {found.Count - validVersions.Length} 个错误目录.");
            await LauncherProfileCompatibility.SynchronizeVersionsAsync(selected.Path, validVersions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (version != _scanVersion)
            {
                return;
            }

            SetEmptyVersionsText("versionSelector.folderReadFailed");
            UpdateSelectedVersion(null, clearFolderMemory: false);
            AnimateEmptyText();
            App.Logger.Error("Versions", $"扫描游戏目录失败: {selected.Path}", exception);
            LauncherToasts.Error(LocalizationManager.Get("versionSelector.readVersionsFailed"));
        }
    }

    private bool UpdateSelectedVersion(LocalMinecraftVersion? selectedVersion, bool clearFolderMemory = true)
    {
        if (selectedVersion is { IsValid: false }) return false;
        string? path = selectedVersion?.FolderPath;
        string? id = selectedVersion?.Id;
        string? selectedFolderPath = App.Settings.SelectedGameFolderPath;
        GameFolderSetting? folder = App.Settings.GameFolders.FirstOrDefault(item => SamePath(item.Path, selectedFolderPath));
        string? rememberedPath = folder?.LastSelectedVersionPath;
        bool currentMatches = (SamePath(App.Settings.SelectedVersionPath, path) || App.Settings.SelectedVersionPath is null && path is null) && App.Settings.SelectedVersionId == id;
        bool memoryMatches = !clearFolderMemory || SamePath(rememberedPath, path) || rememberedPath is null && path is null;
        if (currentMatches && memoryMatches)
        {
            return true;
        }

        GameFolderSetting[] folders = App.Settings.GameFolders.Select(item =>
            folder is not null && SamePath(item.Path, folder.Path) && clearFolderMemory
                ? item with { LastSelectedVersionPath = path }
                : item).ToArray();
        if (!App.UpdateSettings(App.Settings with
            {
                GameFolders = folders,
                SelectedVersionPath = path,
                SelectedVersionId = id
            }))
        {
            return false;
        }

        if (selectedVersion is null) VersionSelectionCleared?.Invoke();
        else VersionSelectionChanged?.Invoke(selectedVersion);
        return true;
    }

    private void SetEmptyVersionsText(string key)
    {
        _emptyVersionsKey = key;
        EmptyVersionsText.Text = LocalizationManager.Get(key);
    }

    private void AnimateEmptyText()
    {
        EmptyVersionsText.Opacity = 1;
        EmptyVersionsText.BeginAnimation(OpacityProperty, EaseOut(0, 1, 180));
    }

    private static DoubleAnimation EaseOut(double from, double to, int milliseconds) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private async void Folder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderRow { IsRenaming: false } row })
        {
            await SelectFolderAsync(row);
        }
    }

    private async void Folder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderRow row })
        {
            await SelectFolderAsync(row);
        }
    }

    private void FolderGear_Click(object sender, RoutedEventArgs e)
    {
        DependencyObject? current = sender as DependencyObject;
        while (current is not null)
        {
            if (current is Border { ContextMenu: not null } folderBorder)
            {
                folderBorder.ContextMenu.PlacementTarget = folderBorder;
                folderBorder.ContextMenu.IsOpen = true;
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void FolderMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.DataContext = (menu.PlacementTarget as FrameworkElement)?.DataContext;
            menu.BeginAnimation(OpacityProperty, EaseOut(0, 1, 140));
        }
    }

    private void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow row })
        {
            return;
        }

        row.IsRenaming = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (FolderList.ItemContainerGenerator.ContainerFromItem(row) is { } container &&
                FindChild<TextBox>(container) is { } editor)
            {
                editor.Focus();
                editor.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void FolderNameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FolderRow row } editor)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            row.IsRenaming = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitRename(row, editor.Text);
            e.Handled = true;
        }
    }

    private void FolderNameEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FolderRow { IsRenaming: true } row } editor)
        {
            CommitRename(row, editor.Text);
        }
    }

    private void CommitRename(FolderRow row, string text)
    {
        string name = text.Trim();
        if (name.Length == 0)
        {
            row.IsRenaming = false;
            return;
        }

        if (name != row.Name)
        {
            GameFolderSetting[] folders = App.Settings.GameFolders
                .Select(folder => SamePath(folder.Path, row.Path) ? folder with { Name = name, NameKey = null, IsNameCustomized = true } : folder)
                .ToArray();
            if (App.UpdateSettings(App.Settings with { GameFolders = folders }))
            {
                row.Name = name;
                if (row.IsSelected)
                {
                    FolderTitle.Text = name;
                }
            }
        }

        row.IsRenaming = false;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow row })
        {
            return;
        }

        if (!Directory.Exists(row.Path))
        {
            LauncherToasts.Error(LocalizationManager.Get("versionSelector.folderMissing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(row.Path) { UseShellExecute = true });
            App.Logger.Information("Versions", $"打开游戏文件夹: {row.Path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            App.Logger.Error("Versions", $"打开游戏文件夹失败: {row.Path}", exception);
            LauncherToasts.Error(LocalizationManager.Get("versionSelector.openFolderFailed"));
        }
    }

    private async void RefreshFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderRow row })
        {
            await SelectFolderAsync(row, refreshSelected: true);
        }
    }

    private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow row } ||
            !await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("versionSelector.removeFolderTitle"), string.Format(LocalizationManager.Get("folder.confirmRemove"), row.Name), confirmText: LocalizationManager.Get("versionSelector.remove")))
        {
            return;
        }

        GameFolderSetting[] folders = App.Settings.GameFolders.Where(folder => !SamePath(folder.Path, row.Path)).ToArray();
        string? selectedPath = row.IsSelected ? folders.FirstOrDefault()?.Path : App.Settings.SelectedGameFolderPath;
        bool clearVersion = SamePath(Path.GetDirectoryName(Path.GetDirectoryName(App.Settings.SelectedVersionPath)), row.Path);
        if (App.UpdateSettings(App.Settings with
            {
                GameFolders = folders,
                SelectedGameFolderPath = selectedPath,
                SelectedVersionPath = clearVersion ? null : App.Settings.SelectedVersionPath,
                SelectedVersionId = clearVersion ? null : App.Settings.SelectedVersionId
            }))
        {
            if (clearVersion)
            {
                VersionSelectionCleared?.Invoke();
            }

            await ReloadFoldersAnimatedAsync();
            await RefreshVersionsAsync();
        }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new() { Title = LocalizationManager.Get("versionSelector.chooseFolder") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        string path = Path.GetFullPath(dialog.FolderName);
        if (App.Settings.GameFolders.All(folder => !SamePath(folder.Path, path)))
        {
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            GameFolderSetting[] folders = [.. App.Settings.GameFolders, new(path, name)];
            if (!App.UpdateSettings(App.Settings with { GameFolders = folders, SelectedGameFolderPath = path }))
            {
                return;
            }
        }
        else if (!App.UpdateSettings(App.Settings with { SelectedGameFolderPath = path }))
        {
            return;
        }

        await LauncherProfileCompatibility.EnsureFolderAsync(path);
        await ReloadFoldersAnimatedAsync();
        await RefreshVersionsAsync();
    }

    private void Version_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VersionRow row }) return;
        if (!row.Version.IsValid)
        {
            LauncherToasts.Error(string.Format(LocalizationManager.Get("versionSelector.invalidSelection"), row.StatusText));
            return;
        }
        if (!UpdateSelectedVersion(row.Version))
        {
            return;
        }

        App.Logger.Information("Versions", $"选择本地版本: {row.Version.Id}");
        VersionChosen?.Invoke(row.Version);
    }

    private async void DeleteVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VersionRow row } || _folders.FirstOrDefault(item => item.IsSelected) is not { } folder)
        {
            return;
        }

        try
        {
            if (await VersionManagementService.DeleteAsync(folder.Path, row.Version)) await RefreshVersionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.Logger.Error("Versions", $"删除本地版本失败: {row.Version.FolderPath}", exception);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("versionSettings.operationFailed"), exception.Message));
        }
    }

    private void VersionSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VersionRow row }) VersionSettingsRequested?.Invoke(row.Version);
    }

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static T? FindChild<T>(DependencyObject parent, string? name = null) where T : FrameworkElement
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found && (name is null || found.Name == name))
            {
                return found;
            }

            if (FindChild<T>(child, name) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    /// <summary>文件夹列表的可通知行模型.</summary>
    public sealed class FolderRow(string path, string name) : INotifyPropertyChanged
    {
        private string _name = name;
        private bool _isSelected;
        private bool _isRenaming;

        public event PropertyChangedEventHandler? PropertyChanged;
        public string Path { get; } = path;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new(nameof(Name)));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            }
        }

        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                _isRenaming = value;
                PropertyChanged?.Invoke(this, new(nameof(IsRenaming)));
            }
        }
    }

    /// <summary>版本列表为共用展示模型补充选中状态.</summary>
    public sealed record VersionRow(LocalMinecraftVersion Version, bool IsSelected, string? CustomDescription = null)
        : LocalVersionPresentation(Version, CustomDescription);
}
