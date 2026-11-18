using System.Collections.Concurrent;
using System.IO;

using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher;

public partial class App
{
    private ModpackFileActivation? _fileActivation;
    private readonly ConcurrentQueue<string> _pendingModpackFiles = new();
    private Exception? _fileActivationError;
    private Exception? _fileActivationWarning;

    private bool PrepareFileActivation(string[] args)
    {
        string? path;
        try
        {
            path = ModpackFileActivation.ParseArguments(args);
        }
        catch (ArgumentException error)
        {
            _fileActivationError = error;
            return false;
        }

        try
        {
            _fileActivation = new ModpackFileActivation(AppContext.BaseDirectory, file =>
            {
                _pendingModpackFiles.Enqueue(file);
                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow window && window.IsLoaded) DeliverModpackFiles(window);
                });
            }, error =>
            {
                if (_logger is null) _fileActivationWarning = error;
                else _logger.Error("FileAssociation", "运行实例之间的整合包传递失败.", error);
            });
            if (path is not null && !_fileActivation.IsPrimary && _fileActivation.TryForwardAsync(path).GetAwaiter().GetResult())
            {
                Shutdown();
                return true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            _fileActivationWarning = error;
        }

        if (path is not null) _pendingModpackFiles.Enqueue(path);
        return false;
    }

    internal static void DeliverModpackFiles(MainWindow window)
    {
        if (Current is not App application) return;
        if (application._fileActivationWarning is { } warning)
        {
            application._fileActivationWarning = null;
            _logger?.Error("FileAssociation", "未能转交给已有实例, 由当前窗口继续处理整合包.", warning);
        }

        if (application._fileActivationError is { } error)
        {
            application._fileActivationError = null;
            Logger.Error("FileAssociation", "整合包打开参数无效.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("fileAssociation.openFailed"), error.Message));
        }

        while (application._pendingModpackFiles.TryDequeue(out string? path)) window.EnqueueAssociatedModpack(path);
    }
}
