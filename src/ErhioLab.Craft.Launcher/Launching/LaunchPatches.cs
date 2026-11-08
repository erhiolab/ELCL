using System.IO;
using System.Security.Cryptography;

namespace ErhioLab.Craft.Launcher.Launching;

internal static class LaunchPatches
{
    internal static async Task<string> ExtractAsync(string name, string directory, CancellationToken cancellationToken)
    {
        await using Stream input = typeof(App).Assembly.GetManifestResourceStream("ErhioLab.Craft.Launcher.Assets.Launch." + name) ?? throw new FileNotFoundException("未包含启动兼容补丁: " + name);
        using MemoryStream buffer = new();
        await input.CopyToAsync(buffer, cancellationToken);
        byte[] bytes = buffer.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string destination = Path.Combine(directory, hash, name);
        if (File.Exists(destination))
        {
            await using var existing = File.OpenRead(destination);
            if (Convert.ToHexString(await SHA256.HashDataAsync(existing, cancellationToken)) == hash) return destination;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, destination, true);
        }
        finally
        {
            File.Delete(temporary);
        }

        return destination;
    }
}
