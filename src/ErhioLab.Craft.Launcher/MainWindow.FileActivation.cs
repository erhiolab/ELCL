using System.Windows;

using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher;

public partial class MainWindow
{
    private AssociatedModpackQueue? _associatedModpackQueue;

    private AssociatedModpackQueue AssociatedModpackQueue => _associatedModpackQueue ??= new(
        path => ImportModpackAsync(path), () => IsLoaded && !_modpackImportBusy, RestoreForFileActivation,
        (path, error) =>
        {
            App.Logger.Error("FileAssociation", $"关联打开整合包失败: {path}.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("fileAssociation.openFailed"), error.Message));
        });

    internal void EnqueueAssociatedModpack(string path)
    {
        if (AssociatedModpackQueue.Enqueue(path)) App.Logger.Information("FileAssociation", $"接收关联打开的整合包: {path}.");
    }

    private void RestoreForFileActivation()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) Show();
        Activate();
    }

    private Task ProcessAssociatedModpacksAsync() => AssociatedModpackQueue.ProcessAsync();
}
