using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Mods;

namespace ErhioLab.Craft.Launcher.Core.Modpacks;

/// <summary>读取通用整合包声明并准备文件; 不选择目录, 不启动下载或 Java 进程.</summary>
public static class ModpackArchive
{
    private const long MaximumFile = 8L * 1024 * 1024 * 1024;
    private const long MaximumTotal = 64L * 1024 * 1024 * 1024;

    public static Task<ModpackDefinition> ReadAsync(string archivePath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var archive = ZipFile.OpenRead(Path.GetFullPath(archivePath));
        if (archive.Entries.Count > 100000) throw new InvalidDataException("整合包文件数量超过 100000.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string entryPath = RelativePath(entry.FullName.TrimEnd('/', '\\'));
            if (!names.Add(entryPath)) throw new InvalidDataException("整合包包含重复路径: " + entryPath);
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("整合包不允许符号链接.");
            total = checked(total + entry.Length);
            if (entry.Length > MaximumFile || total > MaximumTotal) throw new InvalidDataException("整合包解压大小超过限制.");
        }

        var metadata = archive.Entries.Where(entry =>
        {
            var parts = entry.FullName.Replace('\\', '/').Split('/');
            return parts.Length <= 2 && parts[0] is not ("overrides" or "client-overrides" or "minecraft" or ".minecraft") && parts[^1] is "modrinth.index.json" or "manifest.json" or "mmc-pack.json";
        }).ToArray();
        if (metadata.Length != 1) throw new InvalidDataException("无法唯一识别整合包, 请使用 Modrinth、CurseForge 或 MultiMC 格式.");
        var manifest = metadata[0];
        if (manifest.Length > 8 * 1024 * 1024) throw new InvalidDataException("整合包清单超过 8 MiB.");
        using var input = manifest.Open();
        using var metadataBytes = new MemoryStream();
        byte[] metadataBuffer = new byte[16384];
        int metadataCount;
        while ((metadataCount = input.Read(metadataBuffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (metadataBytes.Length + metadataCount > 8 * 1024 * 1024) throw new InvalidDataException("整合包清单超过 8 MiB.");
            metadataBytes.Write(metadataBuffer, 0, metadataCount);
        }

        using var document = JsonDocument.Parse(metadataBytes.ToArray());
        var root = document.RootElement;
        string prefix = manifest.FullName.Replace('\\', '/')[..^(manifest.Name.Length)];
        string name = Text(root, "name", Path.GetFileNameWithoutExtension(archivePath));
        string game;
        ModpackFormat format;
        List<MinecraftLoaderSelection> loaders = [];
        List<ModpackFile> files = [];
        string[] overridePrefixes;
        if (manifest.Name == "modrinth.index.json")
        {
            format = ModpackFormat.Modrinth;
            if (Text(root, "game") != "minecraft" || root.GetProperty("formatVersion").GetInt32() != 1)
                throw new InvalidDataException("不支持该 Modrinth 整合包版本.");
            var dependencies = root.GetProperty("dependencies");
            game = Required(dependencies, "minecraft");
            foreach (var dependency in dependencies.EnumerateObject())
                if (dependency.Name != "minecraft")
                    AddLoader(loaders, dependency.Name, dependency.Value.GetString()!, game);
            foreach (var file in root.GetProperty("files").EnumerateArray())
            {
                if (file.TryGetProperty("env", out var env) && Text(env, "client") == "unsupported") continue;
                var hashes = file.GetProperty("hashes");
                string? sha1 = Hash(hashes, "sha1", 40), sha512 = Hash(hashes, "sha512", 128);
                if (sha1 is null && sha512 is null) throw new InvalidDataException("整合包文件没有校验值.");
                var urls = file.GetProperty("downloads").EnumerateArray().Select(value => DownloadUrl(value.GetString()!)).ToArray();
                if (urls.Length is 0 or > 16) throw new InvalidDataException("整合包文件下载地址数量无效.");
                long? size = file.TryGetProperty("fileSize", out var length) ? length.GetInt64() : null;
                if (size is < 0 or > MaximumFile) throw new InvalidDataException("整合包文件大小无效.");
                files.Add(new(RelativePath(Required(file, "path")), urls, size, sha1, sha512));
            }

            overridePrefixes = [prefix + "overrides/", prefix + "client-overrides/"];
        }
        else if (manifest.Name == "manifest.json")
        {
            format = ModpackFormat.CurseForge;
            if (Text(root, "manifestType") != "minecraftModpack" || root.GetProperty("manifestVersion").GetInt32() != 1)
                throw new InvalidDataException("不支持该 CurseForge 整合包版本.");
            var minecraft = root.GetProperty("minecraft");
            game = Required(minecraft, "version");
            foreach (var loader in minecraft.GetProperty("modLoaders").EnumerateArray())
            {
                string id = Required(loader, "id");
                int separator = id.IndexOf('-');
                if (separator < 1) throw new InvalidDataException("加载器声明无效: " + id);
                AddLoader(loaders, id[..separator], id[(separator + 1)..], game);
            }

            foreach (var file in root.GetProperty("files").EnumerateArray())
            {
                long project = file.GetProperty("projectID").GetInt64(), id = file.GetProperty("fileID").GetInt64();
                if (project <= 0 || id <= 0) throw new InvalidDataException("CurseForge 文件标识无效.");
                files.Add(new("", [], ProjectId: project.ToString(System.Globalization.CultureInfo.InvariantCulture), FileId: id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            overridePrefixes = [prefix + RelativePath(Text(root, "overrides", "overrides")) + "/"];
        }
        else
        {
            format = ModpackFormat.MultiMc;
            if (root.GetProperty("formatVersion").GetInt32() != 1) throw new InvalidDataException("不支持该 MultiMC 整合包版本.");
            var components = root.GetProperty("components").EnumerateArray().ToArray();
            game = components.Where(item => Text(item, "uid") == "net.minecraft").Select(item => Required(item, "version")).SingleOrDefault()
                   ?? throw new InvalidDataException("MultiMC 整合包没有 Minecraft 版本.");
            foreach (var component in components)
            {
                string uid = Required(component, "uid");
                if (uid is "net.minecraft" or "org.lwjgl" or "org.lwjgl3" or "net.fabricmc.intermediary") continue;
                AddLoader(loaders, uid, Required(component, "version"), game);
            }

            overridePrefixes = [prefix + ".minecraft/", prefix + "minecraft/"];
        }

        MinecraftVanillaDownloadPlanner.ValidateVersionName(game);
        MinecraftLoaderInstaller.ValidateSelections(game, loaders);
        if (files.Count > 100000) throw new InvalidDataException("整合包下载文件数量过多.");
        if (files.Sum(file => file.Size ?? 0) > MaximumTotal) throw new InvalidDataException("整合包下载总大小超过 64 GiB.");
        if (files.Where(file => file.Path.Length > 0).GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("整合包包含重复下载目标.");
        List<ModpackOverride> overrides = [];
        foreach (string directory in overridePrefixes)
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.Replace('\\', '/').StartsWith(directory, StringComparison.Ordinal)))
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            string relative = RelativePath(entry.FullName.Replace('\\', '/')[directory.Length..]);
            if (IsLauncherData(relative)) continue;
            overrides.Add(new(entry.FullName, relative, entry.Length));
        }

        return new ModpackDefinition(Path.GetFullPath(archivePath), format, name, game, loaders, files, overrides);
    }, cancellationToken);

    public static async Task<IReadOnlyList<ModpackDownload>> ResolveDownloadsAsync(ModpackDefinition pack, string versionDirectory,
        HttpClient httpClient, string? curseForgeApiKey = null, CommunitySource source = CommunitySource.OfficialFirst,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var client = new ModRepositoryClient(httpClient, curseForgeApiKey, source);
        var downloads = new List<ModpackDownload>();
        var projects = new Dictionary<string, ModProject>();
        foreach (var file in pack.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = file.Path;
            IReadOnlyList<Uri> urls = file.Downloads;
            long? size = file.Size;
            string? sha1 = file.Sha1, sha512 = file.Sha512;
            if (file.ProjectId is { } projectId && file.FileId is { } fileId)
            {
                if (!projects.TryGetValue(projectId, out var project))
                    projects[projectId] = project = await client.GetProjectAsync(ModPlatform.CurseForge, projectId, cancellationToken).ConfigureAwait(false);
                var metadata = await client.GetFileAsync(ModPlatform.CurseForge, projectId, fileId, cancellationToken).ConfigureAwait(false);
                if (!project.CanDownload || metadata.DownloadUri is null)
                    throw new InvalidOperationException($"整合包文件不允许第三方下载: CurseForge {projectId}/{fileId}.");
                if (metadata.FileName.Contains('/') || metadata.FileName.Contains('\\')) throw new InvalidDataException("Mods 文件名无效.");
                path = "mods/" + metadata.FileName;
                urls = [DownloadUrl(metadata.DownloadUri.AbsoluteUri)];
                size = metadata.Size;
                sha1 = metadata.Sha1;
                sha512 = metadata.Sha512;
            }

            if (IsLauncherData(path)) throw new InvalidDataException("整合包下载目标是启动器配置.");
            var sources = urls.SelectMany(url => ModRepositoryClient.DownloadSources(DownloadUrl(url.AbsoluteUri), source)).Distinct().ToArray();
            downloads.Add(new(new DownloadFileRequest(Destination(versionDirectory, path), sources, size, sha1), sha512));
        }

        if (downloads.Select(item => item.Request.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != downloads.Count)
            throw new InvalidDataException("整合包文件解析后出现同名目标.");
        return downloads;
    }

    public static async Task VerifyDownloadsAsync(IReadOnlyList<ModpackDownload> downloads, CancellationToken cancellationToken = default)
    {
        foreach (var file in downloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file.Request.DestinationPath);
            if (!info.Exists || file.Request.ExpectedSize is { } size && info.Length != size) throw new InvalidDataException("整合包文件大小校验失败: " + info.Name);
            await using var input = info.OpenRead();
            if (file.Request.ExpectedSha1 is { } sha1 && !Convert.ToHexStringLower(await SHA1.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).Equals(sha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("整合包文件 SHA-1 校验失败: " + info.Name);
            input.Position = 0;
            if (file.Sha512 is { } sha512 && !Convert.ToHexStringLower(await SHA512.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).Equals(sha512, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("整合包文件 SHA-512 校验失败: " + info.Name);
        }
    }

    public static async Task ExtractOverridesAsync(ModpackDefinition pack, string versionDirectory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        using var archive = ZipFile.OpenRead(pack.ArchivePath);
        long total = Math.Max(1, pack.Overrides.Sum(file => Math.Max(1, file.Size))), completed = 0;
        foreach (var file in pack.Overrides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Destination(versionDirectory, file.Path);
            var entry = archive.GetEntry(file.ArchivePath) ?? throw new InvalidDataException("整合包在导入时发生变化.");
            if (entry.Length != file.Size || file.Size > MaximumFile || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("整合包文件无效或在导入时发生变化.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + ".elcl-import-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var input = entry.Open())
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    byte[] buffer = new byte[65536];
                    long copied = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        copied += count;
                        if (copied > file.Size) throw new InvalidDataException("整合包文件长度超出声明.");
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        progress?.Report((completed + copied) * 100d / total);
                    }

                    if (copied != file.Size) throw new InvalidDataException("整合包文件未完整解压.");
                }

                File.Move(temporary, destination, true);
            }
            finally
            {
                File.Delete(temporary);
            }

            completed += Math.Max(1, file.Size);
        }

        progress?.Report(100);
    }

    private static string Destination(string directory, string relative)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, RelativePath(relative)));
        if (IsLauncherData(relative.Replace('\\', '/'))) throw new InvalidDataException("整合包不能替换启动器配置.");
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("整合包路径超出版本目录.");
        string name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
        if (string.Equals(relative, name + ".json", StringComparison.OrdinalIgnoreCase) || string.Equals(relative, name + ".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("整合包不能替换启动版本元数据或客户端.");
        for (string? parent = Path.GetDirectoryName(full); parent is not null && parent.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length; parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("整合包目标不允许链接目录.");
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("整合包目标不允许链接文件.");
        return full;
    }

    private static string RelativePath(string path)
    {
        path = path.Replace('\\', '/');
        var parts = path.Split('/');
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.Any(character => character < 32 || "<>:\"|?*".Contains(character)) || System.Text.RegularExpressions.Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("整合包包含不安全的路径: " + path);
        return path;
    }

    private static bool IsLauncherData(string path) => path.Split('/')[0].ToLowerInvariant() is "elcl" or "pcl" or "pcl2" or "hmcl" or "launcher_profiles.json" or "launcher_accounts.json";
    private static string Text(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? fallback : fallback;
    private static string Required(JsonElement value, string key) => Text(value, key) is { Length: > 0 } text ? text : throw new InvalidDataException("整合包缺少字段: " + key);

    private static Uri DownloadUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 ? uri : throw new InvalidDataException("整合包下载地址必须使用 HTTPS.");

    private static string? Hash(JsonElement value, string key, int length)
    {
        string text = Text(value, key);
        if (text.Length == 0) return null;
        if (text.Length != length || !text.All(Uri.IsHexDigit)) throw new InvalidDataException("整合包文件校验值无效.");
        return text;
    }

    private static void AddLoader(List<MinecraftLoaderSelection> loaders, string id, string version, string game)
    {
        MinecraftLoaderKind kind = id switch
        {
            "forge" or "net.minecraftforge" => MinecraftLoaderKind.Forge,
            "neoforge" or "net.neoforged" => MinecraftLoaderKind.NeoForge,
            "fabric" or "fabric-loader" or "net.fabricmc.fabric-loader" => MinecraftLoaderKind.Fabric,
            "quilt" or "quilt-loader" or "org.quiltmc.quilt-loader" => MinecraftLoaderKind.Quilt,
            "liteloader" or "com.mumfrey.liteloader" => MinecraftLoaderKind.LiteLoader,
            "optifine" or "optifine.OptiFine" => MinecraftLoaderKind.OptiFine,
            _ => throw new NotSupportedException("不支持整合包组件: " + id)
        };
        if (version.StartsWith(game + "-", StringComparison.Ordinal) && kind == MinecraftLoaderKind.Forge) version = version[(game.Length + 1)..];
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("加载器版本为空.");
        loaders.Add(new(kind, new(version, false)));
    }
}
