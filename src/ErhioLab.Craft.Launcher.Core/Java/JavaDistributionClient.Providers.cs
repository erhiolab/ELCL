using System.Runtime.InteropServices;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Java;

public sealed partial class JavaDistributionClient
{
    private async Task<IReadOnlyList<JavaDownloadVersion>> MojangVersionsAsync(JavaPlatform target, CancellationToken token)
    {
        string key = (target.OperatingSystem, target.Architecture) switch
        {
            ("windows", Architecture.X64) => "windows-x64", ("windows", Architecture.X86) => "windows-x86", ("windows", Architecture.Arm64) => "windows-arm64",
            ("linux", Architecture.X64) => "linux", ("linux", Architecture.X86) => "linux-i386", ("linux", Architecture.Arm64) => "linux-arm64",
            ("macos", Architecture.X64) => "mac-os", ("macos", Architecture.Arm64) => "mac-os-arm64", _ => ""
        };
        if (key.Length == 0) return [];
        using var doc = await JsonAsync(MojangCatalog, token).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty(key, out var platform)) return [];
        List<JavaDownloadVersion> versions = [];
        foreach (var channel in platform.EnumerateObject())
        {
            if (channel.Name == "minecraft-java-exe" || channel.Name.Contains("snapshot", StringComparison.Ordinal)) continue;
            foreach (var row in channel.Value.EnumerateArray())
            {
                var info = row.GetProperty("version");
                string name = Required(info, "name");
                string number = new(name.TakeWhile(char.IsDigit).ToArray());
                if (!int.TryParse(number, out int major) || major < 2) continue;
                var manifest = row.GetProperty("manifest");
                versions.Add(new JavaDownloadVersion(JavaDistribution.Mojang, major, name, JavaPackageType.Jre, channel.Name,
                    target, JavaDownloadFormat.Manifest, Url(manifest, "url"), PositiveSize(manifest, "size"), Date(info, "released"), Sha1: Required(manifest, "sha1")));
            }
        }

        return versions.OrderByDescending(version => version.ReleasedAt).ToArray();
    }

    private async Task<JavaVersionPage> AdoptiumAsync(int major, JavaPlatform target, int page, int pageSize, CancellationToken token)
    {
        string os = target.OperatingSystem == "macos" ? "mac" : target.OperatingSystem;
        string url = $"https://api.adoptium.net/v3/assets/feature_releases/{major}/ga?architecture={Arch(target)}&os={os}&heap_size=normal&jvm_impl=hotspot&project=jdk&vendor=eclipse&page={page}&page_size={pageSize}&sort_order=DESC";
        using var doc = await JsonAsync(url, token).ConfigureAwait(false);
        var releases = doc.RootElement.EnumerateArray().ToArray();
        List<JavaDownloadVersion> versions = [];
        foreach (var release in releases)
        {
            foreach (var binary in release.GetProperty("binaries").EnumerateArray().OrderBy(row => Optional(row, "image_type") == "jre" ? 0 : 1))
            {
                string? type = Optional(binary, "image_type");
                if (type is not ("jre" or "jdk") || Optional(binary, "architecture") != Arch(target) || Optional(binary, "os") != os) continue;
                var package = binary.GetProperty("package");
                string name = Required(package, "name");
                if (!IsArchive(name, target)) continue;
                versions.Add(new JavaDownloadVersion(JavaDistribution.Adoptium, major, Required(release.GetProperty("version_data"), "semver"),
                    type == "jre" ? JavaPackageType.Jre : JavaPackageType.Jdk, name, target, Format(target), Url(package, "link"), PositiveSize(package, "size"),
                    Date(release, "timestamp"), Required(package, "checksum")));
            }
        }

        return new JavaVersionPage(versions, releases.Length == pageSize);
    }

    private static string AzulQuery(JavaPlatform target)
    {
        string arch = target.Architecture switch { Architecture.X64 => "x86_64", Architecture.X86 => "i686", Architecture.Arm64 => "aarch64", _ => "aarch32" };
        string archive = target.OperatingSystem == "windows" ? "zip" : "tar.gz";
        return AzulPackages + $"?os={target.OperatingSystem}&arch={arch}&archive_type={archive}&release_status=ga&availability_types=CA&javafx_bundled=false&crac_supported=false";
    }

    private static bool IsAzulPackage(JsonElement row)
    {
        string name = Required(row, "name");
        return (name.Contains("-jre", StringComparison.Ordinal) || name.Contains("-jdk", StringComparison.Ordinal)) && !name.Contains("-crac-", StringComparison.Ordinal)
                                                                                                                    && !name.Contains("-fx-", StringComparison.Ordinal) && !name.Contains("musl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAzulPlatform(string name, JavaPlatform target)
    {
        string os = target.OperatingSystem switch { "windows" => "win", "macos" => "macosx", _ => "linux" };
        string[] architectures = target.Architecture switch
        {
            Architecture.X64 => ["x64"], Architecture.X86 => ["i686", "i586", "x86"], Architecture.Arm64 => ["aarch64"], _ => ["aarch32hf", "aarch32"]
        };
        return architectures.Any(arch => name.Contains($"-{os}_{arch}.", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<JavaVersionPage> AzulAsync(int major, JavaPlatform target, int page, int pageSize, CancellationToken token)
    {
        using var doc = await JsonAsync(AzulQuery(target) + $"&java_version={major}&page={page + 1}&page_size={pageSize}", token).ConfigureAwait(false);
        var rows = doc.RootElement.EnumerateArray().ToArray();
        var versions = rows.Where(IsAzulPackage).Select(row =>
        {
            string name = Required(row, "name");
            return new JavaDownloadVersion(JavaDistribution.Azul, row.GetProperty("java_version")[0].GetInt32(),
                string.Join('.', row.GetProperty("java_version").EnumerateArray().Select(value => value.GetInt32())),
                name.Contains("-jre", StringComparison.Ordinal) ? JavaPackageType.Jre : JavaPackageType.Jdk, name, target, Format(target), Url(row, "download_url"),
                MetadataUri: new Uri(AzulPackages + Uri.EscapeDataString(Required(row, "package_uuid"))));
        }).Where(version => version.MajorVersion == major && IsArchive(version.Name, target) && MatchesAzulPlatform(version.Name, target)).ToArray();
        return new JavaVersionPage(versions, rows.Length == pageSize);
    }

    private async Task<JavaVersionPage> SemeruAsync(int major, JavaPlatform target, int page, int pageSize, CancellationToken token)
    {
        using var doc = await JsonAsync($"https://api.github.com/repos/ibmruntimes/semeru{major}-binaries/releases?per_page={pageSize}&page={page + 1}", token).ConfigureAwait(false);
        var rows = doc.RootElement.EnumerateArray().ToArray();
        List<JavaDownloadVersion> versions = [];
        string os = target.OperatingSystem == "macos" ? "mac" : target.OperatingSystem;
        string architecture = target.Architecture == Architecture.X86 ? "x86-32" : Arch(target);
        foreach (var release in rows)
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            foreach (var asset in assets.OrderBy(row => Required(row, "name").Contains("-jre_", StringComparison.Ordinal) ? 0 : 1))
            {
                string name = Required(asset, "name");
                if (
                    !IsArchive(name, target) ||
                    !name.StartsWith("ibm-semeru-open-", StringComparison.Ordinal) ||
                    (!name.Contains("-jre_", StringComparison.Ordinal) && !name.Contains("-jdk_", StringComparison.Ordinal)) ||
                    !name.Contains($"_{architecture}_{os}_", StringComparison.Ordinal)
                ) continue;
                var checksum = assets.FirstOrDefault(row => Required(row, "name") == name + ".sha256.txt");
                string? digest = Optional(asset, "digest");
                versions.Add(new JavaDownloadVersion(JavaDistribution.Semeru, major, Required(release, "tag_name").Replace("jdk-", "", StringComparison.Ordinal),
                    name.Contains("-jre_", StringComparison.Ordinal) ? JavaPackageType.Jre : JavaPackageType.Jdk, name, target, Format(target), Url(asset, "browser_download_url"),
                    PositiveSize(asset, "size"), Date(release, "published_at"), digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? digest[7..] : null,
                    ChecksumUri: checksum.ValueKind == JsonValueKind.Object ? Url(checksum, "browser_download_url") : null));
            }
        }

        return new JavaVersionPage(versions, rows.Length == pageSize);
    }
}
