namespace ErhioLab.Craft.Launcher.Dialogs;

/// <summary>主窗口尚未显示时承载同一套自绘弹窗.</summary>
public partial class LauncherDialogWindow
{
    internal string InputValue => DialogView.InputValue;
    /// <summary>初始化独立弹窗.</summary>
    internal LauncherDialogWindow(string title, string message, LauncherDialogLevel level,
        bool requiresConfirmation, string confirmText, string? inputText = null, Func<string, string?>? inputValidator = null)
    {
        InitializeComponent();
        Title = title;
        DialogView.UseStandalonePresentation();
        DialogView.Configure(title, message, level, requiresConfirmation, confirmText, inputText, inputValidator);
        DialogView.Completed += confirmed => DialogResult = confirmed;
        Loaded += (_, _) => DialogView.Open();
    }
}
