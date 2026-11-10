using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Versions;

namespace ErhioLab.Craft.Launcher.Launching;

internal static class LaunchJavaSelector
{
    internal static async Task<JavaInstallation> SelectAsync(MinecraftLaunchVersion version, VersionLaunchOptions options,
        IReadOnlyList<JavaInstallation> candidates, CancellationToken cancellationToken)
    {
        if (options.UseSpecifiedJava)
        {
            var specified = string.IsNullOrWhiteSpace(options.JavaPath) ? null : await JavaDiscovery.ProbeAsync(options.JavaPath, cancellationToken);
            // 独立设置允许强制指定 Java; 仍验证可执行性, 不以兼容性规则替换用户的选择.
            return specified ?? throw new InvalidOperationException(LocalizationManager.Get("versionLaunch.specifiedJavaUnavailable"));
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!version.IsJavaCompatible(candidate.MajorVersion)) continue;
            var probed = await JavaDiscovery.ProbeAsync(candidate.Path, cancellationToken);
            if (probed is not null && version.IsJavaCompatible(probed.MajorVersion)) return probed;
        }

        throw new InvalidOperationException(string.Format(LocalizationManager.Get("gameLaunch.noJava"), version.JavaMajorVersion));
    }
}
