using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Tasks;

/// <summary>由任务管理器组织 Java 下载/校验/解压/登记, 安装内容只写入本次创建的目录.</summary>
internal sealed class JavaInstallTasks
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly JavaDownloadVersion _version;
    private readonly string _work;
    private readonly string _runtime;
    private readonly string _archive;
    private readonly string _destination;
    private JavaDownloadPackage? _package;
    private IReadOnlyList<DownloadFileRequest> _files = [];
    private bool _published;
    private bool _registered;
    private readonly Guid _installationId = Guid.NewGuid();

    private JavaInstallTasks(JavaDownloadVersion version, string parent)
    {
        _version = version;
        string name = $"{version.Distribution}-{version.Version}-{version.PackageType}-{version.Platform.Architecture}";
        name = string.Concat(name.Select(value => Path.GetInvalidFileNameChars().Contains(value) ? '_' : value));
        _destination = Path.Combine(Path.GetFullPath(parent), name[..Math.Min(120, name.Length)]);
        _work = Path.Combine(Path.GetFullPath(parent), ".elcl-java-" + Guid.NewGuid().ToString("N"));
        _runtime = Path.Combine(_work, "runtime");
        _archive = Path.Combine(_work, "package.zip");
    }

    internal static LauncherTask Start(JavaDownloadVersion version, string parent, DownloadOptions options)
    {
        if (version.Platform != JavaPlatform.Current || version.Platform.OperatingSystem != "windows" || version.Format == JavaDownloadFormat.TarGZip)
            throw new InvalidOperationException(L("javaDownload.unsupportedDevice"));
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(L("javaDownload.directoryMissing"));
        var operation = new JavaInstallTasks(version, parent);
        if (Directory.Exists(operation._destination) || File.Exists(operation._destination)) throw new IOException(L("javaDownload.destinationExists"));
        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, operation._destination, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L("javaDownload.alreadyInstalling"));
        LauncherTaskStage Stage(string key, Func<LauncherTaskContext, Task> execute) => new(L(key), execute) { LocalizedName = () => L(key) };
        var task = App.Tasks.Start(string.Format(L("javaDownload.task"), version.Distribution, version.Version),
        [
            Stage("javaDownload.prepare", operation.PrepareAsync),
            Stage("javaDownload.downloadFiles", context => context.DownloadAsync(new DownloadTaskRequest(version.Name, operation._files), options)),
            Stage("javaDownload.deploy", operation.DeployAsync),
            Stage("javaDownload.verify", operation.VerifyAndRegisterAsync)
        ], operation._destination, operation.CleanupAsync);
        task.LocalizedTitle = () => string.Format(L("javaDownload.task"), version.Distribution, version.Version);
        App.Logger.Information("Java", $"创建 Java 安装任务: {version.Distribution} {version.Version}, 架构={version.Platform}, 目录={operation._destination}.");
        return task;
    }

    private async Task PrepareAsync(LauncherTaskContext context)
    {
        var package = await new JavaDistributionClient(Http).GetPackageAsync(_version, context.CancellationToken);
        _package = package;
        Directory.CreateDirectory(_runtime);
        if (_version.Format == JavaDownloadFormat.Manifest)
        {
            List<DownloadFileRequest> files = [];
            foreach (var entry in package.Files)
            {
                string path = SafePath(_runtime, entry.Path);
                if (entry.Type == JavaRuntimeEntryType.Directory) Directory.CreateDirectory(path);
                else if (entry.Type == JavaRuntimeEntryType.File)
                    files.Add(new DownloadFileRequest(path, [entry.DownloadUri ?? throw new InvalidDataException(L("javaDownload.invalidPackage"))], entry.Size, entry.Sha1));
                else throw new InvalidDataException(L("javaDownload.invalidPackage"));
            }

            if (files.Select(file => file.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new InvalidDataException(L("javaDownload.invalidPackage"));
            _files = files;
        }
        else _files = [new DownloadFileRequest(_archive, [package.Version.DownloadUri], package.Version.Size)];

        long bytes = _files.Sum(LauncherTaskContext.EstimatedSize);
        // 解压工作也有独立进度, 权重由下载量估计; 不让准备/验证阶段占据大半进度.
        context.ConfigureStageWeights([1, bytes, _version.Format == JavaDownloadFormat.Manifest ? 1 : Math.Max(1, bytes / 4), 1]);
    }

    private async Task DeployAsync(LauncherTaskContext context)
    {
        if (_version.Format == JavaDownloadFormat.Manifest) return;
        var token = context.CancellationToken;
        await using (var file = new FileStream(_archive, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
        {
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!hash.Equals(_package?.Version.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L("javaDownload.checksumFailed"));
        }

        var progress = new Progress<double>(context.ReportProgress);
        await Task.Run(async () =>
        {
            await using var archive = await ZipFile.OpenReadAsync(_archive, token);
            if (archive.Entries.Count > 100000) throw new InvalidDataException(L("javaDownload.invalidPackage"));
            long total = archive.Entries.Sum(entry => entry.Length);
            if (total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException(L("javaDownload.invalidPackage"));
            long extracted = 0;
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string path = SafePath(_runtime, entry.FullName);
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType is 0xA000 or 0x6000 or 0x2000) throw new InvalidDataException(L("javaDownload.invalidPackage"));
                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var source = await entry.OpenAsync(token);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await source.CopyToAsync(output, token);
                if (output.Length != entry.Length) throw new InvalidDataException(L("javaDownload.invalidPackage"));
                extracted += entry.Length;
                ((IProgress<double>)progress).Report(total == 0 ? 0 : extracted * 100.0 / total);
            }
        }, token);
    }

    private async Task VerifyAndRegisterAsync(LauncherTaskContext context)
    {
        var token = context.CancellationToken;
        string[] candidates = await Task.Run(() => Directory.GetFiles(_runtime, "java.exe", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length).ToArray(), token);
        if (candidates.Length == 0) throw new InvalidDataException(L("javaDownload.verificationFailed"));
        JavaInstallation? verified = null;
        foreach (string path in candidates)
        {
            token.ThrowIfCancellationRequested();
            verified = await JavaDiscovery.ProbeAsync(path, token);
            if (IsExpected(verified)) break;
            verified = null;
        }

        if (verified is null) throw new InvalidDataException(L("javaDownload.verificationFailed"));
        string home = Path.GetDirectoryName(Path.GetDirectoryName(verified.Path))!;
        await File.WriteAllTextAsync(Path.Combine(home, Settings.ManagedJavaMarker.FileName), System.Text.Json.JsonSerializer.Serialize(new Settings.ManagedJavaMarker(_installationId)), token);
        token.ThrowIfCancellationRequested();
        Directory.Move(home, _destination);
        _published = true;
        var installed = await JavaDiscovery.ProbeAsync(Path.Combine(_destination, "bin", "java.exe"), token);
        if (installed is null || !IsExpected(installed)) throw new InvalidDataException(L("javaDownload.verificationFailed"));
        token.ThrowIfCancellationRequested();
        if (!App.JavaCatalog.RegisterInstalled(installed, _destination, _installationId)) throw new IOException(L("javaDownload.saveFailed"));
        _registered = true;
        App.Logger.Information("Java", $"Java 安装并验证完成: {installed.Version}, 路径={installed.Path}.");
        await CleanupAsync();
    }

    private Task CleanupAsync() => Task.Run(() =>
    {
        DeleteOwned(_work, Path.GetDirectoryName(_work)!);
        if (_published && !_registered) DeleteOwned(_destination, Path.GetDirectoryName(_destination)!);
    });

    private bool IsExpected(JavaInstallation? installation) => installation?.MajorVersion == _version.MajorVersion && installation.Architecture.StartsWith(_version.Platform.Architecture is System.Runtime.InteropServices.Architecture.X86 or System.Runtime.InteropServices.Architecture.Arm ? "32" : "64", StringComparison.Ordinal);

    private static void DeleteOwned(string path, string parent)
    {
        string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full == root.TrimEnd(Path.DirectorySeparatorChar)) throw new IOException(L("javaDownload.invalidPackage"));
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }

    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.Contains('\\') || relative.Split('/').Any(part => part is "." or ".."))
            throw new InvalidDataException(L("javaDownload.invalidPackage"));
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L("javaDownload.invalidPackage"));
        return path;
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
