using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Launching;

internal static class LaunchRuntimeCache
{
    internal static async Task<LauncherCache.RuntimeCacheLease> PrepareNativesAsync(LauncherCache cache,
        MinecraftLaunchFilePlan plan, string architecture, Action<string, Exception> onError, CancellationToken token)
    {
        // 保留解包顺序和排除规则, 以实际 JAR 内容而不是版本名称或路径区分本地库.
        var inputs = new List<(string Hash, IReadOnlyList<string> Excludes)>();
        foreach (var native in plan.Natives)
        {
            await using var file = File.OpenRead(native.Path);
            inputs.Add((Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)), native.Excludes));
        }

        string key = "natives-" + LauncherCache.Key(JsonSerializer.Serialize(new
        {
            Format = 1,
            Architecture = architecture,
            Archives = inputs.Select(item => new { item.Hash, item.Excludes })
        }));
        return await cache.GetRuntimeAsync(key, (path, cancellation) => MinecraftLaunchFiles.ExtractNativesAsync(plan, path, cancellation), onError, token).ConfigureAwait(false);
    }
}
