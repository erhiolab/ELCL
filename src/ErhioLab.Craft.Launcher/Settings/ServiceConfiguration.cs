namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>随程序分发的公开客户端配置, 不包含服务端管理密钥.</summary>
internal static class ServiceConfiguration
{
    internal const string DefaultMicrosoftClientId = "f443b610-70eb-465d-a6af-86cca2a46b35";

    internal const string PostHogProjectToken = "phc_wC3EwZtaxTY7W9cPzpX39WyHjTEh68xPyDeDsqhgTf9Y";

    // Project ID 用于管理后台; 事件接收接口通过 Project token 选择项目.
    internal const int PostHogProjectId = 721980;
    internal static readonly Uri PostHogCaptureEndpoint = new("https://us.i.posthog.com/i/v0/e/");

    internal static string MicrosoftClientId
    {
        get
        {
            var clientId = Environment.GetEnvironmentVariable("ELCL_MICROSOFT_CLIENT_ID");
            return string.IsNullOrWhiteSpace(clientId) ? DefaultMicrosoftClientId : clientId.Trim();
        }
    }
}
