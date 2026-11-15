namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>随程序分发的公开客户端配置.</summary>
internal static class ServiceConfiguration
{
    internal const string DefaultMicrosoftClientId = "f443b610-70eb-465d-a6af-86cca2a46b35";

    internal const string DefaultCurseForgeApiKey = "$2a$10$ybWIMmysH4kxR2saHhoW0uMosEpKVG61ts0GTKKIr6puCz3QZaQui";
    internal static string CurseForgeApiKey => Environment.GetEnvironmentVariable("ELCL_CURSEFORGE_API_KEY") is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : DefaultCurseForgeApiKey;

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
