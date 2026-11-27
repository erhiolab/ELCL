using System.IO;
using System.Security.Cryptography;

using ErhioLab.Craft.Launcher.Caching;

namespace ErhioLab.Craft.Launcher.Launching;

internal static class LaunchPatches
{
    internal static async Task<string> ExtractAsync(string name, string directory, CancellationToken cancellationToken)
    {
        var (bytes, hash) = await ReadAsync(name, cancellationToken);
        string destination = Path.Combine(directory, hash, name);
        await LauncherCache.WriteAsync(destination, bytes, cancellationToken);
        return destination;
    }

    internal static async Task<LauncherCache.RuntimeCacheLease> GetSharedAsync(string name, Action<string, Exception> onError, CancellationToken token)
    {
        var (bytes, hash) = await ReadAsync(name, token);
        string key = "patch-" + LauncherCache.Key(name + "\n" + hash);
        return await LauncherCache.Shared.GetRuntimeAsync(key, (path, cancellation) => LauncherCache.WriteAsync(Path.Combine(path, name), bytes, cancellation),
            onError, token);
    }

    private static async Task<(byte[] Bytes, string Hash)> ReadAsync(string name, CancellationToken cancellationToken)
    {
        await using Stream input = typeof(App).Assembly.GetManifestResourceStream("ErhioLab.Craft.Launcher.Assets.Launch." + name) ?? throw new FileNotFoundException("未包含启动兼容补丁: " + name);
        using MemoryStream buffer = new();
        await input.CopyToAsync(buffer, cancellationToken);
        byte[] bytes = buffer.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        return (bytes, hash);
    }
}
