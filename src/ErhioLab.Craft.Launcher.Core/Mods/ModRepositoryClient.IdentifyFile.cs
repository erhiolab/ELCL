using System.Net;
using System.Security.Cryptography;

namespace ErhioLab.Craft.Launcher.Core.Mods;

public sealed partial class ModRepositoryClient
{
    /// <summary>按归档文件的内容精确匹配项目, 不要求项目允许第三方下载.</summary>
    public async Task<ModProject?> IdentifyFileAsync(string filePath, ModProjectKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        cancellationToken.ThrowIfCancellationRequested();
        if ((File.GetAttributes(filePath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("Expected a regular archive file.");
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        string sha1 = Convert.ToHexStringLower(await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        try
        {
            using var document = await RequestAsync(ModPlatform.Modrinth, "/v2/version_file/" + sha1 + "?algorithm=sha1", null, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.GetProperty("files").EnumerateArray().Any(file => Number(file, "size") == stream.Length
                                                                       && Text(file.GetProperty("hashes"), "sha1").Equals(sha1, StringComparison.OrdinalIgnoreCase)))
            {
                var project = await GetProjectAsync(ModPlatform.Modrinth, Text(root, "project_id"), cancellationToken).ConfigureAwait(false);
                if (project.Kind == kind) return project;
            }
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
        }

        if (string.IsNullOrWhiteSpace(_key) || stream.Length > 512L * 1024 * 1024) return null;
        stream.Position = 0;
        uint fingerprint = await ComputeFingerprintAsync(stream, cancellationToken).ConfigureAwait(false);
        using var cf = await RequestAsync(ModPlatform.CurseForge, "/v1/fingerprints/432", new { fingerprints = new[] { fingerprint } }, cancellationToken).ConfigureAwait(false);
        foreach (var match in cf.RootElement.GetProperty("data").GetProperty("exactMatches").EnumerateArray())
        {
            var file = ReadCurseForgeFile(match.GetProperty("file"), false);
            if (file.Size != stream.Length || !string.Equals(file.Sha1, sha1, StringComparison.OrdinalIgnoreCase)) continue;
            var project = await GetProjectAsync(ModPlatform.CurseForge, file.ProjectId, cancellationToken).ConfigureAwait(false);
            if (project.Kind == kind) return project;
        }

        return null;
    }

    private static Task<uint> ComputeFingerprintAsync(Stream stream, CancellationToken token) =>
        Task.Run(() => LocalModManager.Fingerprint(stream, token), token);
}
