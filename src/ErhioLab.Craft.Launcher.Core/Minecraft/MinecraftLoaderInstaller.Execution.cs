using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public sealed partial class MinecraftLoaderInstaller
{
    /// <summary>依赖下载完成后执行客户端处理器. execute 必须等待退出, 取消时结束该进程及子进程.</summary>
    public async Task RunProcessorsAsync(MinecraftLoaderInstallPlan plan, string? javaPath,
        Func<ProcessStartInfo, CancellationToken, Task> execute, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(execute);
        cancellationToken.ThrowIfCancellationRequested();
        var processors = plan.ForgeProfiles.SelectMany(profile => (profile.Metadata["processors"] as JsonArray ?? [])
            .OfType<JsonObject>().Where(IsClient).Select(processor => (Profile: profile, Processor: processor))).ToArray();
        int count = processors.Length + plan.Processes.Count;
        int completed = 0;
        foreach (var item in processors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (metadata, installer, extracted) = item.Profile;
            var processor = item.Processor;
            var outputs = processor["outputs"] as JsonObject;
            if (outputs is { Count: > 0 } && await OutputsValid(plan, metadata, installer, extracted, outputs, cancellationToken).ConfigureAwait(false))
            {
                progress?.Report(++completed * 100d / count);
                continue;
            }

            string[] args = (processor["args"] as JsonArray ?? []).Select(value => Resolve(plan, metadata, installer, extracted, value!.GetValue<string>())).ToArray();
            if (args.Contains("DOWNLOAD_MOJMAPS", StringComparer.Ordinal))
            {
                int outputIndex = Array.IndexOf(args, "--output");
                if (outputIndex < 0 || outputIndex + 1 >= args.Length || !File.Exists(args[outputIndex + 1]))
                    throw new InvalidDataException("Mojang 映射未通过下载队列准备完成.");
            }
            else
            {
                string jar = LibraryPath(plan, processor["jar"]!.GetValue<string>());
                string[] classpath = (processor["classpath"] as JsonArray ?? [])
                    .Select(value => LibraryPath(plan, value!.GetValue<string>())).Append(jar).Distinct().ToArray();
                using var zip = ZipFile.OpenRead(jar);
                string manifest = ReadEntry(zip, "META-INF/MANIFEST.MF").Replace("\r\n ", "").Replace("\n ", "");
                var main = Regex.Match(manifest, @"^Main-Class:\s*(.+)$", RegexOptions.Multiline, TimeSpan.FromSeconds(1));
                if (!main.Success) throw new InvalidDataException("处理器缺少 Main-Class: " + jar);
                foreach (var path in classpath)
                    if (!File.Exists(path))
                        throw new FileNotFoundException("处理器依赖未准备完成.", path);
                plan.Log?.Invoke("执行安装处理器: " + processor["jar"]!.GetValue<string>());
                var start = StartInfo(javaPath, plan.WorkDirectory, string.Join(Path.PathSeparator, classpath), main.Groups[1].Value.Trim(), args);
                await execute(start, cancellationToken).ConfigureAwait(false);
                if (outputs is { Count: > 0 } && !await OutputsValid(plan, metadata, installer, extracted, outputs, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("安装处理器的输出文件校验失败.");
            }

            progress?.Report(++completed * 100d / count);
        }

        foreach (var process in plan.Processes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.Log?.Invoke("执行安装处理器: " + process.MainClass);
            await execute(StartInfo(javaPath, plan.WorkDirectory, process.ClassPath, process.MainClass, process.Args), cancellationToken).ConfigureAwait(false);
            progress?.Report(++completed * 100d / count);
        }

        foreach (var (path, hash) in plan.GeneratedFiles)
            if (!await FileValid(path, hash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("加载器安装文件缺失或校验失败: " + path);
        foreach (var (source, target) in plan.Copies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, false);
            plan.WrittenVersionFiles.Add(target);
        }

        // 新版 Forge/NeoForge 的 patched/extra 等输出不一定列在版本 libraries 中.
        foreach (var profile in plan.ForgeProfiles)
        foreach (string key in new[] { "PATCHED", "MC_EXTRA" })
            if (profile.Metadata["processors"] is JsonArray { Count: > 0 } && profile.Metadata["data"]?[key]?["client"]?.GetValue<string>() is { } value
                                                                           && !await FileValid(Resolve(plan, profile.Metadata, profile.Installer, profile.Extracted, value), null, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("缺少处理器输出: " + key);
        progress?.Report(100);
    }

    /// <summary>所有依赖与处理器成功后, 原子写入最终版本 JSON, 不覆盖已存在的同名版本.</summary>
    public static async Task WriteVersionAsync(MinecraftLoaderInstallPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string folder = Safe(plan.GameDirectory, "versions/" + plan.InstalledName);
        Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, plan.InstalledName + ".json");
        if (File.Exists(destination)) throw new IOException("同名版本已经存在.");
        string baseJsonPath = plan.BaseVersionJsonPath ?? Safe(plan.GameDirectory, $"versions/{plan.GameVersion}/{plan.GameVersion}.json");
        string baseJar = plan.BaseClientJarPath ?? Safe(plan.GameDirectory, $"versions/{plan.GameVersion}/{plan.GameVersion}.jar");
        JsonObject original = ParseObject(await File.ReadAllTextAsync(baseJsonPath, cancellationToken).ConfigureAwait(false));
        JsonObject merged = MinecraftLaunchVersion.Merge(original, plan.VersionJson);
        merged["id"] = plan.InstalledName;
        merged["clientVersion"] = plan.GameVersion;
        merged.Remove("inheritsFrom");
        merged.Remove("jar");
        string client = Path.Combine(folder, plan.InstalledName + ".jar");
        if (!Path.GetFullPath(baseJar).Equals(Path.GetFullPath(client), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            if (File.Exists(client))
            {
                if (!await FileValid(client, original["downloads"]?["client"]?["sha1"]?.GetValue<string>(), cancellationToken).ConfigureAwait(false))
                    throw new IOException("目标目录中已经存在不同的客户端 JAR.");
            }
            else
            {
                string jarTemporary = client + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(baseJar, jarTemporary, false);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(jarTemporary, client, false);
                    plan.WrittenVersionFiles.Add(client);
                }
                finally
                {
                    File.Delete(jarTemporary);
                }
            }
        }

        if (!await FileValid(client, original["downloads"]?["client"]?["sha1"]?.GetValue<string>(), cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("客户端 JAR 不完整, 无法完成安装.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, merged.ToJsonString(JsonOptions), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static bool IsClient(JsonObject processor) => processor["sides"] is not JsonArray sides || sides.Any(side => side?.GetValue<string>() == "client");

    private static ProcessStartInfo StartInfo(string? java, string directory, string classpath, string main, string[] args)
    {
        if (string.IsNullOrWhiteSpace(java)) throw new InvalidOperationException("安装处理器需要调用方提供 Java.");
        var start = new ProcessStartInfo(java)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-Djava.awt.headless=true");
        start.ArgumentList.Add("-cp");
        start.ArgumentList.Add(classpath);
        start.ArgumentList.Add(main);
        foreach (string argument in args) start.ArgumentList.Add(argument);
        return start;
    }

    private static string Resolve(MinecraftLoaderInstallPlan plan, JsonObject metadata, string installer, string extracted, string value, HashSet<string>? resolving = null)
    {
        if (value.StartsWith('\'') && value.EndsWith('\'')) return value[1..^1];
        if (value.StartsWith('[') && value.EndsWith(']')) return LibraryPath(plan, value[1..^1]);
        if (value.StartsWith('/')) return Safe(extracted, value.TrimStart('/'));
        return Regex.Replace(value, @"\{([A-Za-z0-9_]+)\}", match =>
        {
            string key = match.Groups[1].Value;
            return key switch
            {
                "SIDE" => "client",
                "ROOT" => plan.GameDirectory,
                "LIBRARY_DIR" => Path.Combine(plan.GameDirectory, "libraries"),
                "INSTALLER" => installer,
                "MINECRAFT_VERSION" => plan.GameVersion,
                "MINECRAFT_JAR" => plan.BaseClientJarPath ?? Safe(plan.GameDirectory, $"versions/{plan.GameVersion}/{plan.GameVersion}.jar"),
                _ => ResolveData(key)
            };
        }, RegexOptions.None, TimeSpan.FromSeconds(1));

        string ResolveData(string key)
        {
            resolving ??= new HashSet<string>(StringComparer.Ordinal);
            if (resolving.Count >= 32 || !resolving.Add(key)) throw new InvalidDataException("安装器参数存在循环引用或嵌套过深.");
            try
            {
                string data = metadata["data"]?[key]?["client"]?.GetValue<string>() ?? throw new InvalidDataException("未知安装器替换标记: " + key);
                return Resolve(plan, metadata, installer, extracted, data, resolving);
            }
            finally
            {
                resolving.Remove(key);
            }
        }
    }

    private static async Task<bool> OutputsValid(MinecraftLoaderInstallPlan plan, JsonObject metadata, string installer, string extracted,
        JsonObject outputs, CancellationToken token)
    {
        foreach (var output in outputs)
            if (!await FileValid(Resolve(plan, metadata, installer, extracted, output.Key),
                    Resolve(plan, metadata, installer, extracted, output.Value!.GetValue<string>()), token).ConfigureAwait(false))
                return false;
        return true;
    }

    private static async Task<bool> FileValid(string path, string? hash, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
        if (string.IsNullOrWhiteSpace(hash)) return true;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA1.HashDataAsync(stream, token).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}
