using ErhioLab.Craft.Launcher.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Notifications;

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
