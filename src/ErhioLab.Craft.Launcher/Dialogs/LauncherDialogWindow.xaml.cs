using System.Windows;

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

    internal void ConfigureDecision(string declineText, bool longForm)
    {
        ShowInTaskbar = true;
        if (longForm)
        {
            Width = Math.Min(650, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(520, SystemParameters.WorkArea.Height - 24);
        }
        DialogView.ConfigureDecision(declineText, longForm);
    }
}
