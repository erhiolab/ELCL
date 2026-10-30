namespace ErhioLab.Craft.Launcher.Notifications;

/// <summary>一条非阻塞通知的文本和等级.</summary>
/// <param name="Message">显示给用户的简短信息.</param>
/// <param name="Level">决定通知条颜色的等级.</param>
internal sealed record ToastNotification(string Message, ToastLevel Level);
