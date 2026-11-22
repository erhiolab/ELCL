namespace ErhioLab.Craft.Launcher.Dialogs;

internal sealed record LauncherDialogAction(string Label, Func<Task> ExecuteAsync);
