using System.Net;
using System.Security.Cryptography;

namespace ErhioLab.Craft.Launcher.Core.Mods;

public sealed partial class ModRepositoryClient
{
    /// <summary>按本地文件摘要寻找完全匹配且允许下载的资源, 支持 JAR 和 ZIP; 不上传文件内容.</summary>
    public async Task<ModFile?> FindFileAsync(string filePath, bool onlyModrinth = false, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        string sha1 = Convert.ToHexStringLower(await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        try
        {
            using var document = await RequestAsync(ModPlatform.Modrinth, "/v2/version_file/" + sha1 + "?algorithm=sha1", null, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            foreach (var file in root.GetProperty("files").EnumerateArray())
                if (Text(file.GetProperty("hashes"), "sha1").Equals(sha1, StringComparison.OrdinalIgnoreCase)
                    && Url(Text(file, "url")) is { Scheme: "https" } url && file.GetProperty("size").GetInt64() == stream.Length)
                    return new(ModPlatform.Modrinth, Text(root, "project_id"), Text(root, "id"), Text(root, "name"), Text(file, "filename"),
                        url, stream.Length, sha1, Text(file.GetProperty("hashes"), "sha512"), null, "", [], [], []);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
        }

        if (onlyModrinth || string.IsNullOrWhiteSpace(_key) || stream.Length > 512L * 1024 * 1024) return null;
        LocalModInfo local;
        try
        {
            local = await LocalModManager.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        using var cf = await RequestAsync(ModPlatform.CurseForge, "/v1/fingerprints/432", new { fingerprints = new[] { local.CurseForgeFingerprint } }, cancellationToken).ConfigureAwait(false);
        foreach (var match in cf.RootElement.GetProperty("data").GetProperty("exactMatches").EnumerateArray())
        {
            var file = ReadCurseForgeFile(match.GetProperty("file"), true);
            if (file.Sha1?.Equals(sha1, StringComparison.OrdinalIgnoreCase) == true && file.Size == stream.Length && file.DownloadUri is { Scheme: "https" }
                && (await GetProjectAsync(ModPlatform.CurseForge, file.ProjectId, cancellationToken).ConfigureAwait(false)).CanDownload) return file;
        }

        return null;
    }
}
