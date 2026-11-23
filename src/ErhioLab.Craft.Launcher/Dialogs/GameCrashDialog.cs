using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Launching;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Views.Tasks;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Dialogs;

internal static class GameCrashDialog
{
    internal static void Queue(GameCrashReport report)
    {
        if (Application.Current is not { } application || application.Dispatcher.HasShutdownStarted) return;
        // 先完成退出处理和释放日志句柄, 不让弹窗阻塞进程清理.
        _ = application.Dispatcher.InvokeAsync(async () =>
        {
            if (application.MainWindow is not MainWindow { IsVisible: true } owner) return;
            try
            {
                var analyzed = await report.AnalyzeAsync();
                if (analyzed.Diagnosis is not null || analyzed.ModKeywords.Count > 0)
                    App.Logger.Information("CrashReport", $"本地日志分析匹配: PID={report.ProcessId}, 类型={analyzed.Diagnosis?.Kind}, Mods 关键词数={analyzed.ModKeywords.Count}.");
                await ShowAsync(owner, analyzed);
            }
            catch (InvalidOperationException error)
            {
                App.Logger.Error("CrashReport", $"无法显示游戏错误弹窗: PID={report.ProcessId}.", error);
            }
        }, DispatcherPriority.Background);
    }

    private static Task<bool> ShowAsync(MainWindow owner, GameCrashReport report)
    {
        var paragraphs = new List<string>();
        var actions = new List<LauncherDialogAction>
        {
            new(L("viewLog"), () => ViewLogAsync(owner, report)),
            new(L("export"), () => ExportAsync(owner, report))
        };
        if (report.Diagnosis is { } diagnosis)
        {
            string details = diagnosis is { Kind: MinecraftCrashKind.JavaTooOld, RequiredJavaMajorVersion: { } required }
                ? string.Format(L("analysis.requiresJava"), required)
                : L("analysis." + diagnosis.Kind);
            paragraphs.Add(details);
        }
        if (report.ModKeywords.Count > 0)
        {
            string label = L(report.ModKeywords[0].Kind == MinecraftCrashModKeywordKind.StackPackage ? "stackKeywords" : "modKeywords");
            var values = report.ModKeywords.Select(keyword => keyword.Value).ToArray();
            paragraphs.Add($"{label}{Environment.NewLine}{string.Join(Environment.NewLine, values)}{Environment.NewLine}{L("keywordHint")}");
            actions.Add(new(L("copyKeywords"), () =>
            {
                Clipboard.SetText(string.Join(Environment.NewLine, values));
                LauncherToasts.Success(L("keywordsCopied"));
                return Task.CompletedTask;
            }));
        }

        paragraphs.Add(L("message"));
        return owner.ShowDialogAsync(L("title"), string.Join(Environment.NewLine + Environment.NewLine, paragraphs),
            LauncherDialogLevel.Error, false, L("dismiss"), actions: actions);
    }

    private static Task ViewLogAsync(MainWindow owner, GameCrashReport report)
    {
        if (!File.Exists(report.LogPath))
        {
            App.Logger.Warning("CrashReport", $"本次游戏日志已不存在: {report.LogPath}.");
            LauncherToasts.Error(L("logUnavailable"));
            return Task.CompletedTask;
        }

        var viewer = new GameLogView();
        string title = string.Format(LocalizationManager.Get("gameLogs.title"), report.VersionName);
        viewer.ShowFile(report.LogPath, title, snapshot: true, backToolTipKey: "gameCrash.closeLog");
        var window = new Window
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Width = Math.Min(960, SystemParameters.WorkArea.Width),
            Height = Math.Min(660, SystemParameters.WorkArea.Height),
            Content = viewer
        };
        window.SetResourceReference(Control.BackgroundProperty, "InkBrush");
        viewer.BackRequested += window.Close;
        window.Closed += (_, _) => viewer.CloseFile();
        App.Logger.Information("CrashReport", $"查看错误实例日志: PID={report.ProcessId}, 日志={report.LogPath}.");
        window.ShowDialog();
        return Task.CompletedTask;
    }

    private static async Task ExportAsync(MainWindow owner, GameCrashReport report)
    {
        try
        {
            var picker = new SaveFileDialog
            {
                Title = L("export"), Filter = L("zipFilter"), FileName = report.SuggestedFileName, DefaultExt = ".zip", AddExtension = true
            };
            if (picker.ShowDialog(owner) != true) return;
            App.Logger.Information("CrashReport", $"开始导出游戏错误报告: PID={report.ProcessId}, 目标={picker.FileName}.");
            await report.ExportAsync(picker.FileName);
            App.Logger.Information("CrashReport", $"游戏错误报告已导出: PID={report.ProcessId}, 目标={picker.FileName}.");
            LauncherToasts.Success(L("exported"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                          or InvalidOperationException or Win32Exception or System.Security.SecurityException)
        {
            App.Logger.Error("CrashReport", $"导出游戏错误报告失败: PID={report.ProcessId}.", error);
            LauncherToasts.Error(L("exportFailed"));
        }
    }

    private static string L(string key) => LocalizationManager.Get("gameCrash." + key);
}
