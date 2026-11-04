using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Java;

/// <summary>从环境变量/系统登记信息与有界的常见目录发现 Java.</summary>
public static partial class JavaDiscovery
{
    private const int MaxDirectories = 1200;
    private const int MaxCandidates = 80;
    private static string ExecutableName => OperatingSystem.IsWindows() ? "java.exe" : "java";
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>发现并验证本机 Java; 调用方可传入额外的安装根目录.</summary>
    public static async Task<IReadOnlyList<JavaInstallation>> FindAsync(IEnumerable<string>? additionalRoots = null, CancellationToken cancellationToken = default)
    {
        HashSet<string> candidates = new(PathComparer);
        List<string> hintHomes = [];
        foreach (string path in Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [])
        {
            string directory = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                AddExecutable(candidates, Path.Combine(directory, ExecutableName));
                hintHomes.Add(string.Equals(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)), "bin", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(directory) ?? directory : directory);
            }
        }

        foreach (string name in new[] { "JAVA_HOME", "JDK_HOME" })
        {
            string? home = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(home))
            {
                string expanded = Environment.ExpandEnvironmentVariables(home.Trim().Trim('"'));
                hintHomes.Add(expanded);
                AddJavaAt(candidates, expanded);
            }
        }

        IEnumerable<string> roots = (additionalRoots ?? []).Concat(SiblingRoots(hintHomes)).Concat(KnownRoots());
        foreach (string root in roots.Distinct(PathComparer))
        {
            ScanKnownRoot(root, candidates, cancellationToken);
            if (candidates.Count >= MaxCandidates)
            {
                break;
            }
        }

        using SemaphoreSlim gate = new(4);
        Task<JavaInstallation?>[] checks = candidates.Select(async path =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await ProbeAsync(path, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();
        JavaInstallation?[] checkedCandidates = await Task.WhenAll(checks);
        JavaInstallation[] valid = checkedCandidates.OfType<JavaInstallation>().ToArray();
        HashSet<string> validPaths = new(valid.Select(java => java.Path), PathComparer);
        return valid.Where(java => !IsNestedJreDuplicate(java.Path, validPaths))
            .OrderByDescending(java => java.MajorVersion)
            .ThenBy(java => java.Path, PathComparer)
            .ToArray();
    }

    /// <summary>
    /// 深度遍历所有可访问的本地目录并验证找到的 Java. 可能耗时很久并产生大量磁盘 I/O;
    /// 只应由用户主动触发, 不要在应用启动或普通刷新时调用.
    /// </summary>
    /// <param name="roots">要搜索的根目录; 为空时搜索所有就绪的本地磁盘.</param>
    /// <param name="progress">每扫描约 256 个目录报告一次进度, 完成扫描时再报告一次.</param>
    /// <param name="cancellationToken">取消扫描和候选验证.</param>
    public static async Task<IReadOnlyList<JavaInstallation>> FindExhaustiveAsync(
        IEnumerable<string>? roots = null,
        IProgress<JavaDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string[] searchRoots = (roots ?? DefaultExhaustiveRoots())
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToArray();
        HashSet<string> candidates = await Task.Run(() => ScanExhaustively(searchRoots, progress, cancellationToken), cancellationToken);

        ConcurrentBag<JavaInstallation> valid = [];
        await Parallel.ForEachAsync(candidates, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 4
        }, async (path, token) =>
        {
            if (await ProbeAsync(path, token) is { } installation)
            {
                valid.Add(installation);
            }
        });

        JavaInstallation[] installations = valid.ToArray();
        HashSet<string> validPaths = new(installations.Select(java => java.Path), PathComparer);
        return installations.Where(java => !IsNestedJreDuplicate(java.Path, validPaths))
            .OrderByDescending(java => java.MajorVersion)
            .ThenBy(java => java.Path, PathComparer)
            .ToArray();
    }

    private static IEnumerable<string> DefaultExhaustiveRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [Path.GetPathRoot(Environment.CurrentDirectory) ?? "/"];
        }

        return DriveInfo.GetDrives()
            .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName);
    }

    private static HashSet<string> ScanExhaustively(
        IEnumerable<string> roots, IProgress<JavaDiscoveryProgress>? progress, CancellationToken cancellationToken)
    {
        HashSet<string> candidates = new(PathComparer);
        Stack<string> pending = new();
        foreach (string root in roots)
        {
            if (Directory.Exists(root))
            {
                pending.Push(root);
            }
        }

        long scanned = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            scanned++;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, ExecutableName))
                {
                    candidates.Add(file);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 当前目录的文件不可读时, 仍继续搜索其子目录.
            }

            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(child);
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        // 单个子目录消失或不可读时跳过它, 保留其余兄弟目录.
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 无权限列出子目录时继续搜索其他目录.
            }

            if ((scanned & 255) == 0)
            {
                progress?.Report(new JavaDiscoveryProgress(scanned, candidates.Count, directory));
            }
        }

        progress?.Report(new JavaDiscoveryProgress(scanned, candidates.Count, string.Empty));
        return candidates;
    }

    /// <summary>运行 Java 以确认可用性并读取版本/主版本号与架构.</summary>
    public static async Task<JavaInstallation?> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) || !string.Equals(Path.GetFileName(fullPath), ExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            fullPath = CanonicalizeExecutable(fullPath);

            ProcessStartInfo startInfo = new(fullPath)
            {
                Arguments = "-XshowSettings:properties -version",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using Process process = Process.Start(startInfo)!;
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }

            string output = await standardError + "\n" + await standardOutput;
            Match versionMatch = VersionPattern().Match(output);
            if (process.ExitCode != 0 || !versionMatch.Success)
            {
                return null;
            }

            string version = versionMatch.Groups[1].Success ? versionMatch.Groups[1].Value : versionMatch.Groups[2].Value;
            string majorText = version.StartsWith("1.", StringComparison.Ordinal) ? version.Split('.')[1] : version.Split('.')[0];
            if (!int.TryParse(majorText, out int major) || major <= 0)
            {
                return null;
            }

            Match architectureMatch = ArchitecturePattern().Match(output);
            string architecture = architectureMatch.Success ? architectureMatch.Groups[1].Value + " 位" : output.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) ? "64 位" : "未知位数";
            return new JavaInstallation(fullPath, version, major, architecture);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IEnumerable<string> KnownRoots()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!OperatingSystem.IsWindows())
        {
            return new[]
            {
                "/usr/lib/jvm", "/usr/java", "/opt/java", "/opt/jdk",
                Path.Combine(user, ".sdkman", "candidates", "java"),
                Path.Combine(user, ".jdks"),
                Path.Combine(user, ".local", "share", "mise", "installs", "java"),
                "/Library/Java/JavaVirtualMachines",
                Path.Combine(user, "Library", "Java", "JavaVirtualMachines")
            };
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return new[]
        {
            Path.Combine(appData, ".minecraft", "runtime"),
            Path.Combine(appData, ".hmcl", "java"),
            Path.Combine(appData, "PrismLauncher", "java"),
            Path.Combine(appData, "ATLauncher", "runtimes", "minecraft"),
            Path.Combine(appData, "ModrinthApp", "meta", "java_versions"),
            Path.Combine(local, ".ftba", "bin", "runtime"),
            Path.Combine(user, ".jdks"),
            Path.Combine(user, "curseforge", "minecraft", "Install", "runtime"),
            Path.Combine(user, "scoop", "apps"),
            Path.Combine(programFilesX86, "Minecraft Launcher", "runtime"),
            Path.Combine(programFilesX86, "Minecraft", "runtime"),
            Path.Combine(programFiles, "Java"),
            Path.Combine(programFiles, "Eclipse Adoptium"),
            Path.Combine(programFiles, "Amazon Corretto"),
            Path.Combine(programFiles, "Zulu"),
            Path.Combine(programFiles, "Microsoft"),
            Path.Combine(programFiles, "AdoptOpenJDK"),
            Path.Combine(local, "Programs", "Eclipse Adoptium"),
            Path.Combine(local, "Programs", "Java")
        };
    }

    private static IEnumerable<string> SiblingRoots(IEnumerable<string> hintHomes)
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (string home in hintHomes)
        {
            DirectoryInfo directory;
            try
            {
                directory = new DirectoryInfo(Path.GetFullPath(home));
                if (!File.Exists(Path.Combine(directory.FullName, "bin", ExecutableName)))
                {
                    continue;
                }

                directory = directory.ResolveLinkTarget(true) as DirectoryInfo ?? directory;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            DirectoryInfo? parent = directory.Parent;
            if (parent is null || parent.Parent is null ||
                new[] { user, programFiles, programFilesX86 }.Any(broad => !string.IsNullOrEmpty(broad) && string.Equals(parent.FullName, broad, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return parent.FullName;
        }
    }

    private static void ScanKnownRoot(string root, HashSet<string> candidates, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        Queue<(string Directory, int Depth)> pending = new();
        pending.Enqueue((root, 0));
        int visited = 0;
        while (pending.Count > 0 && visited++ < MaxDirectories && candidates.Count < MaxCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string directory, int depth) = pending.Dequeue();
            AddJavaAt(candidates, directory);
            if (depth >= 4)
            {
                continue;
            }

            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Enqueue((child, depth + 1));
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 单个安装位置不可读时继续尝试其他来源.
            }
        }
    }

    private static void AddJavaAt(HashSet<string> candidates, string folder)
    {
        AddExecutable(candidates, Path.Combine(folder, "bin", ExecutableName));
        AddExecutable(candidates, Path.Combine(folder, "jre", "bin", ExecutableName));
        if (OperatingSystem.IsMacOS())
        {
            AddExecutable(candidates, Path.Combine(folder, "Contents", "Home", "bin", ExecutableName));
        }
    }

    private static void AddExecutable(HashSet<string> candidates, string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                candidates.Add(CanonicalizeExecutable(fullPath));
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // 无效环境变量不阻断其他候选路径.
        }
    }

    private static string CanonicalizeExecutable(string path)
    {
        try
        {
            FileInfo file = new(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 && file.ResolveLinkTarget(true) is FileInfo targetFile)
            {
                path = targetFile.FullName;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 保留原路径继续验证.
        }

        string? binPath = Path.GetDirectoryName(path);
        DirectoryInfo? bin = binPath is null ? null : new DirectoryInfo(binPath);
        DirectoryInfo? home = bin?.Parent;
        if (home is null || (home.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return path;
        }

        try
        {
            if (home.ResolveLinkTarget(true) is DirectoryInfo target)
            {
                return Path.Combine(target.FullName, "bin", Path.GetFileName(path));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 链接无法解析时仍可尝试原路径.
        }

        return path;
    }

    private static bool IsNestedJreDuplicate(string path, HashSet<string> validPaths)
    {
        DirectoryInfo? bin = new FileInfo(path).Directory;
        DirectoryInfo? jre = bin?.Parent;
        DirectoryInfo? home = jre?.Parent;
        return home is not null && string.Equals(jre!.Name, "jre", StringComparison.OrdinalIgnoreCase)
               && validPaths.Contains(Path.Combine(home.FullName, "bin", ExecutableName));
    }

    [GeneratedRegex("java\\.version\\s*=\\s*([^\\s]+)|\\bversion\\s+\"([^\"\\r\\n]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"sun\.arch\.data\.model\s*=\s*(\d+)")]
    private static partial Regex ArchitecturePattern();
}
