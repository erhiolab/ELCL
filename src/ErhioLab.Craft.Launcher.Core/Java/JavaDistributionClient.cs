using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Java;

/// <summary>查询 Java 发行方.</summary>
public sealed partial class JavaDistributionClient(HttpClient httpClient)
{
    private readonly HttpClient _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private const string MojangCatalog = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
    private const string AzulPackages = "https://api.azul.com/metadata/v1/zulu/packages/";

    /// <summary>查询发行方的主版本目录; 具体平台和可部署包在 GetVersionsAsync 中继续筛选.</summary>
    public async Task<IReadOnlyList<int>> GetMajorVersionsAsync(JavaDistribution distribution, JavaPlatform? platform = null, CancellationToken cancellationToken = default)
    {
        var target = ValidatePlatform(platform ?? JavaPlatform.Current);
        List<int> majors = [];
        switch (distribution)
        {
            case JavaDistribution.Mojang:
                majors.AddRange((await MojangVersionsAsync(target, cancellationToken).ConfigureAwait(false)).Select(version => version.MajorVersion));
                break;
            case JavaDistribution.Adoptium:
                using (var doc = await JsonAsync("https://api.adoptium.net/v3/info/available_releases", cancellationToken).ConfigureAwait(false))
                    majors.AddRange(doc.RootElement.GetProperty("available_releases").EnumerateArray().Select(value => value.GetInt32()));
                break;
            case JavaDistribution.Azul:
                for (int page = 1;; page++)
                {
                    using var doc = await JsonAsync(AzulQuery(target) + $"&page={page}&page_size=1000", cancellationToken).ConfigureAwait(false);
                    var rows = doc.RootElement.EnumerateArray().ToArray();
                    majors.AddRange(rows.Where(row => IsAzulPackage(row) && MatchesAzulPlatform(Required(row, "name"), target)).Select(row => row.GetProperty("java_version")[0].GetInt32()));
                    if (rows.Length < 1000) break;
                }

                break;
            case JavaDistribution.Semeru:
                for (int page = 1;; page++)
                {
                    using var doc = await JsonAsync($"https://api.github.com/orgs/ibmruntimes/repos?per_page=100&type=public&page={page}", cancellationToken).ConfigureAwait(false);
                    var rows = doc.RootElement.EnumerateArray().ToArray();
                    foreach (var row in rows)
                    {
                        var match = SemeruRepository().Match(Required(row, "name"));
                        if (match.Success) majors.Add(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                    }

                    if (rows.Length < 100) break;
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(distribution));
        }

        return majors.Where(value => value > 0).Distinct().OrderDescending().ToArray();
    }

    /// <summary>分页获取指定设备的正式版 JRE/JDK, Windows 仅返回 ZIP, Linux/macOS 返回 tar.gz.</summary>
    public async Task<JavaVersionPage> GetVersionsAsync(JavaDistribution distribution, int majorVersion, JavaPlatform? platform = null,
        int page = 0, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (majorVersion <= 0) throw new ArgumentOutOfRangeException(nameof(majorVersion));
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var target = ValidatePlatform(platform ?? JavaPlatform.Current);
        try
        {
            switch (distribution)
            {
                case JavaDistribution.Mojang:
                    var all = (await MojangVersionsAsync(target, cancellationToken).ConfigureAwait(false)).Where(version => version.MajorVersion == majorVersion).ToArray();
                    return new JavaVersionPage(all.Skip(checked(page * pageSize)).Take(pageSize).ToArray(), all.Length > (long)(page + 1) * pageSize);
                case JavaDistribution.Adoptium: return await AdoptiumAsync(majorVersion, target, page, pageSize, cancellationToken).ConfigureAwait(false);
                case JavaDistribution.Azul: return await AzulAsync(majorVersion, target, page, pageSize, cancellationToken).ConfigureAwait(false);
                case JavaDistribution.Semeru: return await SemeruAsync(majorVersion, target, page, pageSize, cancellationToken).ConfigureAwait(false);
                default: throw new ArgumentOutOfRangeException(nameof(distribution));
            }
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return new JavaVersionPage([], false);
        }
    }

    /// <summary>补齐 Azul 的包校验信息或获取并校验 Mojang 文件清单; 不下载 Java 二进制/不创建目录.</summary>
    public async Task<JavaDownloadPackage> GetPackageAsync(JavaDownloadVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Format == JavaDownloadFormat.Manifest)
        {
            if (version.Distribution != JavaDistribution.Mojang) throw new InvalidDataException("不支持的 Java 文件清单.");
            byte[] bytes = await BytesAsync(version.DownloadUri, cancellationToken).ConfigureAwait(false);
            if (version.Size is { } size && size != bytes.LongLength) throw new InvalidDataException("Java 清单大小不匹配.");
            CheckHash(version.Sha1, 40);
            if (!Convert.ToHexString(SHA1.HashData(bytes)).Equals(version.Sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Java 清单 SHA-1 不匹配.");
            using var doc = JsonDocument.Parse(bytes);
            List<JavaRuntimeEntry> files = [];
            foreach (var item in doc.RootElement.GetProperty("files").EnumerateObject())
            {
                var row = item.Value;
                switch (Required(row, "type"))
                {
                    case "directory": files.Add(new JavaRuntimeEntry(item.Name, JavaRuntimeEntryType.Directory)); break;
                    case "link": files.Add(new JavaRuntimeEntry(item.Name, JavaRuntimeEntryType.Link, LinkTarget: Required(row, "target"))); break;
                    case "file":
                        var raw = row.GetProperty("downloads").GetProperty("raw");
                        string hash = Required(raw, "sha1");
                        CheckHash(hash, 40);
                        files.Add(new JavaRuntimeEntry(item.Name, JavaRuntimeEntryType.File, Url(raw, "url"), PositiveSize(raw, "size"), hash,
                            row.TryGetProperty("executable", out var executable) && executable.GetBoolean()));
                        break;
                    default: throw new InvalidDataException("未知的 Java 清单条目类型.");
                }
            }

            if (!files.Any(file => file.Type == JavaRuntimeEntryType.File)) throw new InvalidDataException("Java 清单中没有文件.");
            return new JavaDownloadPackage(version, files);
        }

        if (version.Distribution == JavaDistribution.Azul)
        {
            if (version.MetadataUri is null) throw new InvalidDataException("缺少 Azul 包详情地址.");
            using var doc = await JsonAsync(version.MetadataUri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            version = version with { Sha256 = Required(root, "sha256_hash"), Size = PositiveSize(root, "size"), ReleasedAt = Date(root, "build_date") };
        }

        if (version.Sha256 is null && version.ChecksumUri is { } checksum)
        {
            string text = System.Text.Encoding.UTF8.GetString(await BytesAsync(checksum, cancellationToken).ConfigureAwait(false));
            version = version with { Sha256 = Sha256Text().Match(text).Value };
        }

        CheckHash(version.Sha256, 64);
        return new JavaDownloadPackage(version, []);
    }

    private async Task<JsonDocument> JsonAsync(string url, CancellationToken token) => JsonDocument.Parse(await BytesAsync(new Uri(url), token).ConfigureAwait(false));

    private async Task<byte[]> BytesAsync(Uri url, CancellationToken token)
    {
        for (int attempt = 0;; attempt++)
        {
            try
            {
                return await ReadBytesAsync(url, token).ConfigureAwait(false);
            }
            catch (Exception error) when (attempt < 2 && !token.IsCancellationRequested && IsTransient(error))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), token).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception error) => error is IOException or TaskCanceledException
                                                        || error is HttpRequestException request && (request.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)request.StatusCode >= 500);

    private async Task<byte[]> ReadBytesAsync(Uri url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0) request.Headers.UserAgent.ParseAdd("ELCLC/" + CoreInfo.Version);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        const int maximum = 32 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Java 元数据过大.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Java 元数据过大.");
            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }

    private static JavaPlatform ValidatePlatform(JavaPlatform platform)
    {
        if (platform.OperatingSystem is not ("windows" or "linux" or "macos")) throw new ArgumentException("不支持的 Java 操作系统.", nameof(platform));
        if (platform.Architecture is not (Architecture.X64 or Architecture.X86 or Architecture.Arm64 or Architecture.Arm)) throw new ArgumentException("不支持的 Java 架构.", nameof(platform));
        return platform;
    }

    private static string Arch(JavaPlatform target) => target.Architecture switch { Architecture.X64 => "x64", Architecture.X86 => "x86", Architecture.Arm64 => "aarch64", _ => "arm" };
    private static JavaDownloadFormat Format(JavaPlatform target) => target.OperatingSystem == "windows" ? JavaDownloadFormat.Zip : JavaDownloadFormat.TarGZip;
    private static bool IsArchive(string name, JavaPlatform target) => name.EndsWith(target.OperatingSystem == "windows" ? ".zip" : ".tar.gz", StringComparison.OrdinalIgnoreCase);
    private static string Required(JsonElement row, string key) => row.GetProperty(key).GetString() is { Length: > 0 } value ? value : throw new JsonException($"Java 元数据缺少 {key}.");
    private static string? Optional(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? PositiveSize(JsonElement row, string key) => row.TryGetProperty(key, out var size) && size.TryGetInt64(out long value) && value >= 0 ? value : null;
    private static DateTimeOffset? Date(JsonElement row, string key) => DateTimeOffset.TryParse(Optional(row, key), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    private static Uri Url(JsonElement row, string key) => Uri.TryCreate(Required(row, key), UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri : throw new JsonException("Java 下载地址必须使用 HTTPS.");

    private static void CheckHash(string? hash, int length)
    {
        if (hash?.Length != length || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Java 校验信息缺失或无效.");
    }

    [GeneratedRegex(@"^semeru(\d+)-binaries$")]
    private static partial Regex SemeruRepository();

    [GeneratedRegex(@"\b[0-9a-fA-F]{64}\b")]
    private static partial Regex Sha256Text();
}
