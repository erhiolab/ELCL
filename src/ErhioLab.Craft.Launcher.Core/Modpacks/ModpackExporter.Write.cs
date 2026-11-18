using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Mods;

namespace ErhioLab.Craft.Launcher.Core.Modpacks;

public static partial class ModpackExporter
{
    public static async Task<ModpackExportResult> WriteAsync(ModpackExportScan scan, ModpackExportOptions options, string outputPath,
        ModRepositoryClient? repository = null, IProgress<ModpackExportProgress>? progress = null,
        Action<string, Exception>? onLookupFailure = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Name) || options.Name.Length > 128 || options.Name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(options.Version) || options.Version.Length > 64 || options.Version.Any(char.IsControl)) throw new ArgumentException("整合包名称或版本无效.", nameof(options));
        if (!scan.Dependencies.TryGetValue("minecraft", out string? game) || game != scan.GameVersion) throw new InvalidDataException("缺少必选的 Minecraft 依赖.");
        string output = Path.GetFullPath(outputPath);
        if (output.StartsWith(Path.GetFullPath(scan.VersionDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请将整合包保存到当前版本文件夹之外.");
        if (!options.OverwriteExisting && File.Exists(output)) throw new IOException("目标整合包已存在.");
        var known = scan.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var selected = options.SelectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(path =>
            known.TryGetValue(path, out var file) && file.CanSelect && Category(file.Path) != ModpackExportCategory.Runtime
            && !file.Path.Equals(Path.GetFileName(scan.VersionDirectory) + ".json", StringComparison.OrdinalIgnoreCase)
            && !file.Path.Equals(Path.GetFileName(scan.VersionDirectory) + ".jar", StringComparison.OrdinalIgnoreCase)
                ? file
                : throw new InvalidDataException("选择了不存在或不允许导出的文件: " + path)).ToArray();
        if (selected.Length > MaximumFiles || selected.Any(file => file.Size is < 0 or > MaximumFile) || selected.Sum(file => file.Size) > MaximumTotal)
            throw new InvalidDataException("导出内容超过整合包文件数量或大小限制.");
        foreach (var file in selected) ValidateFile(scan, file);
        if (!options.BundleResources && repository is null) throw new ArgumentNullException(nameof(repository), "未直接打包资源时需要提供资源查询客户端.");

        int failures = 0;
        using SemaphoreSlim lookupGate = new(4);
        var lookups = selected.Where(file => !options.BundleResources && IsResource(file)).ToDictionary(file => file.Path, file => Lookup(file), StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(lookups.Values).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        string temporary = output + ".elcl-export-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        int embedded = 0, referenced = 0;
        long total = Math.Max(1, selected.Sum(file => Math.Max(1, file.Size))), completed = 0;
        List<object> downloads = [];
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string source = ValidateFile(scan, file);
                    await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                    using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
                    byte[] buffer = new byte[65536];
                    long read = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        sha1.AppendData(buffer, 0, count);
                        sha512.AppendData(buffer, 0, count);
                        read += count;
                        progress?.Report(new((completed + read * 0.5) * 100d / total, file.Path));
                    }

                    if (read != file.Size) throw new IOException("导出文件长度发生变化: " + file.Path);
                    string hash1 = Convert.ToHexStringLower(sha1.GetHashAndReset()), hash512 = Convert.ToHexStringLower(sha512.GetHashAndReset());
                    ModFile? remote = lookups.TryGetValue(file.Path, out var lookup) ? await lookup.ConfigureAwait(false) : null;
                    if (remote is { DownloadUri: { Scheme: "https", UserInfo.Length: 0 } url } && remote.Size == file.Size && string.Equals(remote.Sha1, hash1, StringComparison.OrdinalIgnoreCase))
                    {
                        downloads.Add(new { path = file.Path, hashes = new { sha1 = hash1, sha512 = hash512 }, env = new { client = "required", server = "unsupported" }, downloads = new[] { url.AbsoluteUri }, fileSize = file.Size });
                        referenced++;
                    }
                    else
                    {
                        input.Position = 0;
                        var entry = archive.CreateEntry("overrides/" + file.Path, CompressionLevel.Optimal);
                        await using var destination = entry.Open();
                        long copied = 0;
                        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                            copied += count;
                            progress?.Report(new((completed + file.Size * 0.5 + copied * 0.5) * 100d / total, file.Path));
                        }

                        if (copied != file.Size) throw new IOException("导出文件未完整复制: " + file.Path);
                        embedded++;
                    }

                    completed += Math.Max(1, file.Size);
                    progress?.Report(new(completed * 100d / total, file.Path));
                }

                byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, game = "minecraft", versionId = options.Version.Trim(), name = options.Name.Trim(), files = downloads, dependencies = scan.Dependencies }, new JsonSerializerOptions { WriteIndented = true });
                if (manifest.Length > 8 * 1024 * 1024) throw new InvalidDataException("整合包清单超过 8 MiB.");
                await using var metadata = archive.CreateEntry("modrinth.index.json").Open();
                await metadata.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, options.OverwriteExisting);
            progress?.Report(new(100, ""));
            return new(embedded, referenced, failures, selected.Sum(file => file.Size));
        }
        finally
        {
            File.Delete(temporary);
        }

        async Task<ModFile?> Lookup(ModpackExportFile file)
        {
            await lookupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                progress?.Report(new(0, file.Path));
                return await repository!.FindFileAsync(ValidateFile(scan, file), options.OnlyModrinth, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or IOException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or TaskCanceledException)
            {
                Interlocked.Increment(ref failures);
                onLookupFailure?.Invoke(file.Path, error);
                return null;
            }
            finally
            {
                lookupGate.Release();
            }
        }
    }

    private static bool IsResource(ModpackExportFile file) => file.Category is ModpackExportCategory.Mods or ModpackExportCategory.ResourcePacks or ModpackExportCategory.ShaderPacks
                                                              && (file.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private static string ValidateFile(ModpackExportScan scan, ModpackExportFile file)
    {
        string path = SourcePath(scan.VersionDirectory, file.Path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != file.Size || info.LastWriteTimeUtc != file.LastWriteTimeUtc) throw new IOException("版本内容已变化, 请重新扫描后导出: " + file.Path);
        return path;
    }
}
