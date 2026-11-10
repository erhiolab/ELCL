using ErhioLab.Craft.Launcher.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.SystemTools;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.More;

/// <summary>通过共享下载队列获取用户指定的单个文件.</summary>
public partial class ToolboxPage
{
    private string? _automaticFileName;

    /// <summary>初始化默认保存位置和下载表单.</summary>
    public ToolboxPage()
    {
        InitializeComponent();
        SaveFolderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        App.MemoryOptimizer.Changed += RefreshMemoryButton;
        RefreshMemoryButton();
    }

    private void RefreshMemoryButton() => OptimizeMemoryButton.IsEnabled = !App.MemoryOptimizer.IsOptimizing;

    private async void OptimizeMemory_Click(object sender, RoutedEventArgs e)
    {
        try { await App.MemoryOptimizer.OptimizeAsync(showNotifications: true); }
        catch (Exception) { /* 共用服务已记录异常并通知.  */ }
    }

    private async void DailyLuck_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Settings.DailyLuckDisclaimerAccepted)
        {
            if (!await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("toolbox.dailyLuck"), LocalizationManager.Get("toolbox.luckDisclaimer"))) return;
            if (!App.UpdateSettings(App.Settings with { DailyLuckDisclaimerAccepted = true })) return;
        }
        var date = DateOnly.FromDateTime(DateTime.Now);
        int score = ToolboxTools.DailyLuck(date);
        string messageKey = ToolboxTools.DailyLuckMessageKey(score,
            App.Settings.DailyLuckDate == date ? App.Settings.DailyLuckMessageKey : null);
        if (App.Settings.DailyLuckDate != date || App.Settings.DailyLuckMessageKey != messageKey)
        {
            App.Logger.Information("Toolbox", $"开始保存今日人品文案: date={date:yyyy-MM-dd}, message={messageKey}.");
            if (!App.UpdateSettings(App.Settings with { DailyLuckDate = date, DailyLuckMessageKey = messageKey })) return;
            App.Logger.Information("Toolbox", $"今日人品文案已保存: date={date:yyyy-MM-dd}, message={messageKey}.");
        }
        string message = LocalizationManager.Get(messageKey);
        App.Logger.Information("Toolbox", $"查看今日人品: date={date:yyyy-MM-dd}, score={score}, message={messageKey}.");
        LauncherDialogs.Information(string.Format(LocalizationManager.Get("toolbox.luckTitle"), date),
            string.Format(LocalizationManager.Get("toolbox.luckResult"), score, message));
    }

    private async void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        if (App.Tasks.Tasks.Any(task => task.IsActive))
        {
            LauncherToasts.Information(LocalizationManager.Get("toolbox.cleanupBusy"));
            return;
        }
        Window? owner = Window.GetWindow(this);
        if (owner is not null) owner.IsEnabled = false;
        CleanupButton.IsEnabled = false;
        string[] games = App.Settings.GameFolders.Select(folder => folder.Path).ToArray();
        string[] active = App.Tasks.Tasks.Where(task => task.IsActive && task.TargetPath is not null).Select(task => task.TargetPath!).ToArray();
        App.Logger.Information("Toolbox", $"开始清理游戏垃圾: 游戏目录数={games.Length}.");
        LauncherToasts.Information(LocalizationManager.Get("toolbox.cleanupStarted"));
        bool cleaned = false;
        try
        {
            var result = await Task.Run(() => ToolboxTools.Clean(games, Path.Combine(AppContext.BaseDirectory, "ELCL"), active,
                (path, error) => App.Logger.Error("Toolbox", $"无法清理文件: {path}.", error)));
            App.Logger.Information("Toolbox", $"清理完成: bytes={result.Bytes}, files={result.Files}, skipped={result.Skipped}, failed={result.Failed}.");
            cleaned = true;
            LauncherToasts.Success(string.Format(LocalizationManager.Get("toolbox.cleanupRestarting"), result.Files,
                DataSizeFormatter.Format(result.Bytes, App.Settings.MemoryUnit, "0.00", minimumPower: 2)));
            await Task.Delay(600);
            App.Restart(result);
        }
        catch (Exception error)
        {
            App.Logger.Error("Toolbox", cleaned ? "清理后自动重启失败." : "清理游戏垃圾失败.", error);
            LauncherToasts.Error(LocalizationManager.Get(cleaned ? "toolbox.cleanupRestartFailed" : "toolbox.cleanupFailed"));
        }
        finally
        {
            CleanupButton.IsEnabled = true;
            if (owner is not null) owner.IsEnabled = true;
        }
    }

    private void DownloadUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool valid = Uri.TryCreate(DownloadUrlBox.Text.Trim(), UriKind.Absolute, out Uri? url)
                     && url.Scheme is "https" or "http";
        if (StartButton is not null)
        {
            StartButton.IsEnabled = valid;
        }

        if (FileNameBox is null || !valid || url is null)
        {
            return;
        }

        string candidate = Uri.UnescapeDataString(url.AbsolutePath.Split('/').LastOrDefault() ?? string.Empty);
        string name = new(candidate.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "download";
        }

        if (string.IsNullOrWhiteSpace(FileNameBox.Text) || FileNameBox.Text == _automaticFileName)
        {
            FileNameBox.Text = name;
        }

        _automaticFileName = name;
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = LocalizationManager.Get("toolbox.chooseDownloadDestination"),
            InitialDirectory = Directory.Exists(SaveFolderBox.Text) ? SaveFolderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            SaveFolderBox.Text = dialog.FolderName;
        }
    }

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(DownloadUrlBox.Text.Trim(), UriKind.Absolute, out Uri? url)
            || url.Scheme is not ("https" or "http"))
        {
            LauncherToasts.Error(LocalizationManager.Get("toolbox.invalidDownloadUrl"));
            return;
        }

        string folder = SaveFolderBox.Text.Trim();
        string fileName = FileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder) ||
            string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or ".." || fileName != Path.GetFileName(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            LauncherToasts.Error(LocalizationManager.Get("toolbox.invalidSavePathOrName"));
            return;
        }

        string destination;
        try
        {
            destination = Path.Combine(Path.GetFullPath(folder), fileName);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            LauncherToasts.Error(LocalizationManager.Get("toolbox.invalidSaveLocation"));
            return;
        }

        if (File.Exists(destination)
            && !await LauncherDialogs.ConfirmAsync(LocalizationManager.Get("toolbox.overwriteExistingFile"), string.Format(LocalizationManager.Get("toolbox.confirmOverwrite"), fileName), confirmText: LocalizationManager.Get("toolbox.overwrite")))
        {
            return;
        }

        DownloadOptions options = new(App.Settings.DownloadMaxConnections,
            (long)(App.Settings.DownloadSpeedLimitMiBPerSecond * 1024 * 1024));
        try
        {
            App.Tasks.StartCustomDownload(destination, url, options);
            LauncherToasts.Information(string.Format(LocalizationManager.Get("toolbox.taskCreated"), fileName));
        }
        catch (Exception error)
        {
            LauncherToasts.Error(string.Format(LocalizationManager.Get("toolbox.taskFailed"), error.Message));
            App.Logger.Error("Tasks", $"创建自定义下载任务失败: {fileName}.", error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = SaveFolderBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            LauncherToasts.Error(LocalizationManager.Get("toolbox.saveFolderMissing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("Download", $"无法打开文件夹: {folder}.", error);
            LauncherToasts.Error(LocalizationManager.Get("toolbox.openSaveFolderFailed"));
        }
    }
}
