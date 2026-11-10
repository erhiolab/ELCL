using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Views.Settings;

public partial class LaunchSettingsPage
{
    private static readonly GameGarbageCollector[] GarbageCollectorChoices =
    [
        GameGarbageCollector.PreferZgc, GameGarbageCollector.PreferGenerationalZgc,
        GameGarbageCollector.StandardG1, GameGarbageCollector.TunedG1, GameGarbageCollector.Custom
    ];

    private readonly DispatcherTimer _launchTextSaveTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(400)
    };

    private TextBox? _placeholderPreviewSource;
    private LocalMinecraftVersion? _placeholderVersion;
    private string? _placeholderVersionFolder;
    private int _placeholderLoadRevision;

    private void InitializeAdvancedControls()
    {
        _launchTextSaveTimer.Tick += (_, _) => CommitLaunchText();
        Loaded += async (_, _) => await RefreshPlaceholderVersionAsync();
        Unloaded += (_, _) =>
        {
            CommitLaunchText();
            _placeholderLoadRevision++;
            _placeholderVersionFolder = null;
            _placeholderVersion = null;
        };
    }

    private void RefreshAdvancedControls(LauncherSettings settings)
    {
        _launchTextSaveTimer.Stop();
        GameWindowTitleBox.Text = settings.GameWindowTitle;
        JvmArgumentsBox.Text = settings.JvmArguments;
        GameArgumentsBox.Text = settings.GameArguments;
        PreLaunchCommandBox.Text = settings.PreLaunchCommand;
        WaitForCommandBox.IsChecked = settings.WaitForPreLaunchCommand;
        DisableJavaWrapperBox.IsChecked = settings.DisableJavaLaunchWrapper;
        DisableLwjglAgentBox.IsChecked = settings.DisableLwjglUnsafeAgent;
        HighPerformanceGpuBox.IsChecked = settings.PreferHighPerformanceGpu;
        VerifyCertificatesBox.IsChecked = settings.VerifyMicrosoftCertificates;
        SkipFileVerificationBox.IsChecked = settings.SkipFileVerification;
        GarbageCollectorBox.ItemsSource = new[]
        {
            LocalizationManager.Get("launchSettings.gcPreferZgc"),
            LocalizationManager.Get("launchSettings.gcPreferGenerationalZgc"),
            LocalizationManager.Get("launchSettings.gcStandardG1"),
            LocalizationManager.Get("launchSettings.gcTunedG1"),
            LocalizationManager.Get("launchSettings.gcCustom")
        };
        GarbageCollectorBox.SelectedIndex = Array.IndexOf(GarbageCollectorChoices, settings.GarbageCollector);
    }

    private void LaunchText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializingLaunchOptions) return;
        _dirtyLaunchText.Add((TextBox)sender);
        _launchTextSaveTimer.Stop();
        _launchTextSaveTimer.Start();
        UpdateLaunchPreviews();
    }

    private void LaunchText_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _placeholderPreviewSource = (TextBox)sender;
        UpdateLaunchPreviews();
    }

    private void LaunchText_LostFocus(object sender, RoutedEventArgs e) => CommitLaunchText();

    private void LaunchTitle_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitLaunchText();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private bool CommitLaunchText()
    {
        _launchTextSaveTimer.Stop();
        if (_initializingLaunchOptions || _dirtyLaunchText.Count == 0) return true;
        LauncherSettings settings = CurrentSettings;
        LauncherSettings next = settings with
        {
            GameWindowTitle = _dirtyLaunchText.Contains(GameWindowTitleBox) ? GameWindowTitleBox.Text : settings.GameWindowTitle,
            JvmArguments = _dirtyLaunchText.Contains(JvmArgumentsBox) ? JvmArgumentsBox.Text : settings.JvmArguments,
            GameArguments = _dirtyLaunchText.Contains(GameArgumentsBox) ? GameArgumentsBox.Text : settings.GameArguments,
            PreLaunchCommand = _dirtyLaunchText.Contains(PreLaunchCommandBox) ? PreLaunchCommandBox.Text : settings.PreLaunchCommand
        };
        _dirtyLaunchText.Clear();
        if (next == settings) return true;

        // 参数和命令可能含用户自填的敏感信息, 日志只记录变更, 不输出文本内容.
        bool saved = SaveLaunchOptions(next, "游戏窗口标题及高级启动文本已保存.");
        UpdateLaunchPreviews();
        return saved;
    }

    private void AdvancedChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingLaunchOptions || !CommitLaunchText()) return;
        LauncherSettings settings = CurrentSettings;
        bool enabled = ((CheckBox)sender).IsChecked == true;
        LauncherSettings next = ((CheckBox)sender).Name switch
        {
            nameof(WaitForCommandBox) => settings with { WaitForPreLaunchCommand = enabled },
            nameof(DisableJavaWrapperBox) => settings with { DisableJavaLaunchWrapper = enabled },
            nameof(DisableLwjglAgentBox) => settings with { DisableLwjglUnsafeAgent = enabled },
            nameof(HighPerformanceGpuBox) => settings with { PreferHighPerformanceGpu = enabled },
            nameof(VerifyCertificatesBox) => settings with { VerifyMicrosoftCertificates = enabled },
            nameof(SkipFileVerificationBox) => settings with { SkipFileVerification = enabled },
            _ => settings
        };
        if (next == settings) return;
        SaveLaunchOptions(next, $"高级启动开关已保存: 等待命令={next.WaitForPreLaunchCommand}, 禁用 JLW={next.DisableJavaLaunchWrapper}, " + $"禁用 LUA={next.DisableLwjglUnsafeAgent}, 高性能显卡={next.PreferHighPerformanceGpu}, 验证证书={next.VerifyMicrosoftCertificates}.");
    }

    private void GarbageCollectorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLaunchOptions || GarbageCollectorBox.SelectedIndex < 0 || !CommitLaunchText()) return;
        GameGarbageCollector gc = GarbageCollectorChoices[GarbageCollectorBox.SelectedIndex];
        if (gc == CurrentSettings.GarbageCollector) return;
        SaveLaunchOptions(CurrentSettings with { GarbageCollector = gc }, $"游戏内存管理预设已保存: {gc}.");
    }

    private void ResetJvmArguments_Click(object sender, RoutedEventArgs e)
    {
        JvmArgumentsBox.Text = IsVersionMode ? App.Settings.JvmArguments : LauncherSettings.DefaultJvmArguments;
        if (CommitLaunchText()) App.Logger.Information("Settings", IsVersionMode ? "已还原全局附加 JVM 参数." : "已还原默认附加 JVM 参数.");
    }

    private async Task RefreshPlaceholderVersionAsync()
    {
        if (IsVersionMode)
        {
            _placeholderVersion = _editingVersion;
            UpdateLaunchPreviews();
            return;
        }
        string? selected = App.Settings.SelectedVersionPath;
        if (string.Equals(selected, _placeholderVersionFolder, StringComparison.OrdinalIgnoreCase)) return;
        _placeholderVersionFolder = selected;
        _placeholderVersion = null;
        int revision = ++_placeholderLoadRevision;
        string? gameDirectory = App.Settings.SelectedGameFolderPath ?? (selected is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(selected)));
        if (selected is null || gameDirectory is null)
        {
            UpdateLaunchPreviews();
            return;
        }

        try
        {
            LocalMinecraftVersion? version = await Task.Run(() => LocalMinecraftVersionScanner.Scan(gameDirectory, (path, error) => App.Logger.Warning("Settings", $"替换标记预览跳过无法读取的版本文件: {path}, {error.Message}")).FirstOrDefault(value => string.Equals(value.FolderPath, selected, StringComparison.OrdinalIgnoreCase)));
            if (revision != _placeholderLoadRevision || !IsLoaded || !IsVisible) return;
            _placeholderVersion = version;
            UpdateLaunchPreviews();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            App.Logger.Error("Settings", "无法读取替换标记预览所需的版本信息.", error);
        }
    }

    private void UpdateLaunchPreviews()
    {
        if (_initializingLaunchOptions) return;
        LauncherSettings previewSettings = CurrentSettings;
        if (IsVersionMode) previewSettings = previewSettings with
        {
            SelectedGameFolderPath = _versionGameDirectory,
            SelectedVersionPath = _versionDirectory,
            SelectedVersionId = _editingVersion?.Id,
            JavaInstallations = _versionOptions.UseSpecifiedJava
                ? App.Settings.JavaInstallations.Where(java => string.Equals(java.Path, _versionOptions.JavaPath, StringComparison.OrdinalIgnoreCase)).ToArray()
                : App.Settings.JavaInstallations
        };
        MinecraftLaunchPlaceholderContext context = LaunchPlaceholderPreview.Create(_placeholderVersion, previewSettings);
        string title = GameWindowTitleBox.Text;
        GameWindowTitlePreview.Text = string.IsNullOrWhiteSpace(title)
            ? LocalizationManager.Get("launchSettings.defaultWindowTitle")
            : string.Format(LocalizationManager.Get("launchSettings.windowTitlePreview"), MinecraftLaunchPlaceholders.Expand(title, context));
        WaitForCommandReveal.IsExpanded = !string.IsNullOrWhiteSpace(PreLaunchCommandBox.Text);
        string template = _placeholderPreviewSource?.Text ?? "";
        bool show = template.Contains('{');
        PlaceholderPreviewReveal.IsExpanded = show;
        if (!show) return;
        string expanded = MinecraftLaunchPlaceholders.Expand(template, context);
        PlaceholderPreviewBox.Text = expanded;
        IReadOnlyList<string> unknown = MinecraftLaunchPlaceholders.GetUnresolvedTokens(expanded);
        UnknownPlaceholdersText.Text = unknown.Count == 0
            ? ""
            : string.Format(LocalizationManager.Get("launchSettings.unknownPlaceholders"), string.Join(", ", unknown));
        UnknownPlaceholdersText.Visibility = unknown.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
