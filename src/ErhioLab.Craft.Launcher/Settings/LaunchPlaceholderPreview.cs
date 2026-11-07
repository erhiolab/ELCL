using System.Globalization;
using System.IO;
using System.Reflection;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>为设置页预览提供当前选择的信息; 实际启动应重新选择 Java 并生成启动上下文.</summary>
internal static class LaunchPlaceholderPreview
{
    internal static MinecraftLaunchPlaceholderContext Create(LocalMinecraftVersion? version)
    {
        LauncherSettings settings = App.Settings;
        var account = App.Accounts.SelectedAccount;
        string gameDirectory = settings.SelectedGameFolderPath ?? Path.Combine(AppContext.BaseDirectory, ".minecraft");
        string? java = settings.JavaInstallations.FirstOrDefault()?.Path;
        string? loader = version?.Loader;
        return new MinecraftLaunchPlaceholderContext
        {
            LauncherName = "ELCL",
            LauncherVersion = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(App).Assembly.GetName().Version?.ToString(),
            LauncherDirectory = AppContext.BaseDirectory,
            LauncherExecutablePath = Environment.ProcessPath,
            CacheDirectory = Path.Combine(AppContext.BaseDirectory, "ELCL", "cache"),
            JavaDirectory = GetJavaDirectory(java),
            GameDirectory = gameDirectory,
            GameInstanceDirectory = gameDirectory,
            VersionDirectory = settings.SelectedVersionPath,
            VersionName = version?.Id ?? settings.SelectedVersionId,
            GameVersion = version?.GameVersion ?? version?.Id ?? settings.SelectedVersionId,
            ForgeVersion = loader == "Forge" ? version?.LoaderVersion : null,
            FabricVersion = loader == "Fabric" ? version?.LoaderVersion : null,
            NeoForgeVersion = loader == "NeoForge" ? version?.LoaderVersion : null,
            QuiltVersion = loader == "Quilt" ? version?.LoaderVersion : null,
            HasLiteLoader = loader == "LiteLoader",
            PlayerName = account?.UserName,
            PlayerUuid = account?.Uuid,
            LoginType = account is null ? null : LocalizationManager.Get(account.IsMicrosoft ? "launch.microsoftAccount" : "account.offlineShort"),
            Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [nameof(settings.GameWindowWidth)] = settings.GameWindowWidth.ToString(CultureInfo.InvariantCulture),
                [nameof(settings.GameWindowHeight)] = settings.GameWindowHeight.ToString(CultureInfo.InvariantCulture),
                [nameof(settings.CustomMemoryPercent)] = settings.CustomMemoryPercent.ToString(CultureInfo.InvariantCulture),
                [nameof(settings.GamePriority)] = settings.GamePriority.ToString(),
                [nameof(settings.WindowMode)] = settings.WindowMode.ToString(),
                [nameof(settings.AutoAllocateMemory)] = settings.AutoAllocateMemory ? "true" : "false"
            },
            NativeValues = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["version_type"] = version?.Type
            }
        };
    }

    private static string? GetJavaDirectory(string? executable)
    {
        try
        {
            return executable is null ? null : Path.GetDirectoryName(executable);
        }
        catch (Exception error) when (error is ArgumentException or PathTooLongException)
        {
            // 导入配置中的无效 Java 交由 Java 探测验证, 预览按未选择 Java 处理. 
            return null;
        }
    }
}
