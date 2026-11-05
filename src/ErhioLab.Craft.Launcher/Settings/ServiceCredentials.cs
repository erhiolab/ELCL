using System.IO;
using System.Text.Json;
using ErhioLab.Craft.Launcher.Core.Storage;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>启动器调用外部服务所需的应用配置, 和用户设置/账户数据分开.</summary>
internal sealed record ServiceCredentials
{
    internal const string DefaultMicrosoftClientId = "f443b610-70eb-465d-a6af-86cca2a46b35";

    public string MicrosoftClientId { get; init; } = DefaultMicrosoftClientId;

    internal static ServiceCredentials Load(string path)
    {
        JsonFileStore<ServiceCredentials> store = new(path, new JsonSerializerOptions { WriteIndented = true });
        ServiceCredentials settings;
        try
        {
            settings = store.Load(new ServiceCredentials());
            if (!File.Exists(path)) store.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Logger.Error("Services", "读取服务配置失败, 本次使用默认 Client ID; 原文件已保留.", exception);
            settings = new ServiceCredentials();
        }

        string? microsoftOverride = Environment.GetEnvironmentVariable("ELCL_MICROSOFT_CLIENT_ID");
        return new ServiceCredentials { MicrosoftClientId = string.IsNullOrWhiteSpace(microsoftOverride) ? settings.MicrosoftClientId : microsoftOverride.Trim() };
    }
}
