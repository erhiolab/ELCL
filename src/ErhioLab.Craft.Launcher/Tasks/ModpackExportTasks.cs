using System.IO;
using System.IO.Compression;

using ErhioLab.Craft.Launcher.Core.Modpacks;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Mods;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Views.Launch;

namespace ErhioLab.Craft.Launcher.Tasks;

internal static class ModpackExportTasks
{
    internal static LauncherTask Start(ModpackExportScan scan, ModpackExportOptions options, bool includeLauncher, string outputPath)
    {
        if (App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, scan.VersionDirectory, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L("packExport.versionBusy"));
        string output = Path.GetFullPath(outputPath);
        if (output.StartsWith(scan.VersionDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(L("packExport.outsideVersion"));
        string work = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "pack-export", Guid.NewGuid().ToString("N")));
        string innerName = VersionExportPage.SafeName(options.Name + "-" + options.Version) + ".mrpack";
        string pack = includeLauncher ? Path.Combine(work, innerName) : output;
        ModpackExportResult? result = null;
        var selectedPaths = options.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string current = "";
        var write = new LauncherTaskStage(L("packExport.writeStage"), async context =>
        {
            Directory.CreateDirectory(work);
            try
            {
                var progress = new Progress<ModpackExportProgress>(value =>
                {
                    current = value.Path;
                    context.ReportProgress(value.Percent);
                    context.Owner.Stages[0].RefreshLocalization();
                });
                result = await Task.Run(() => ModpackExporter.WriteAsync(scan, options, pack, options.BundleResources ? null : CommunityMods.Client(),
                    progress,
                    (path, error) => App.Logger.Error("Modpacks", $"导出时资源来源未匹配, 改为打包本地文件: {path}.", error), context.CancellationToken), context.CancellationToken);
                App.Logger.Information("Modpacks", $"整合包已写入: {pack}, 内嵌={result.EmbeddedFiles}, 引用={result.ReferencedFiles}, 来源查询失败={result.LookupFailures}.");
            }
            catch
            {
                Cleanup();
                throw;
            }
        }) { LocalizedName = () => L("packExport.writeStage") + (current.Length == 0 ? "" : " · " + current), PlannedBytes = Math.Max(1, scan.Files.Where(file => selectedPaths.Contains(file.Path)).Sum(file => file.Size)) };
        List<LauncherTaskStage> stages = [write];
        if (includeLauncher)
        {
            stages.Add(new LauncherTaskStage(L("packExport.launcherStage"), async context =>
            {
                var progress = new Progress<double>(context.ReportProgress);
                try
                {
                    await Task.Run(() => BundleAsync(pack, innerName, output, options.OverwriteExisting, progress, context.CancellationToken), context.CancellationToken);
                }
                finally
                {
                    Cleanup();
                }
            }) { LocalizedName = () => L("packExport.launcherStage"), PlannedBytes = Math.Max(1, ProgramFiles().Sum(file => new FileInfo(file).Length)) });
        }

        var task = App.Tasks.Start(string.Format(L("packExport.task"), options.Name), stages, scan.VersionDirectory,
            cleanupOnRemove: () =>
            {
                Cleanup();
                return Task.CompletedTask;
            }, onCompleted: () =>
            {
                Cleanup();
                App.Logger.Information("Modpacks", $"导出完成: {output}.");
                LauncherToasts.Success(string.Format(L("packExport.completed"), output, result?.EmbeddedFiles ?? 0, result?.ReferencedFiles ?? 0));
            });
        task.LocalizedTitle = () => string.Format(L("packExport.task"), options.Name);
        App.Logger.Information("Modpacks", $"创建导出任务: 版本={scan.VersionDirectory}, 文件={options.SelectedPaths.Count}, 附带启动器={includeLauncher}, 打包资源={options.BundleResources}, 仅 Modrinth={options.OnlyModrinth}, 目标={output}.");
        return task;

        void Cleanup()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ELCL", "cache", "pack-export")) + Path.DirectorySeparatorChar;
            if (!work.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("导出临时目录超出缓存目录.");
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                App.Logger.Error("Modpacks", $"未能清理导出缓存: {work}.", error);
            }
        }
    }

    private static async Task BundleAsync(string pack, string innerName, string output, bool overwrite, IProgress<double> progress, CancellationToken token)
    {
        var files = ProgramFiles().Select(path => (Path: path, Entry: "Launcher/" + Path.GetRelativePath(AppContext.BaseDirectory, path).Replace('\\', '/')))
            .Append((Path: pack, Entry: innerName)).ToArray();
        long total = Math.Max(1, files.Sum(file => new FileInfo(file.Path).Length)), completed = 0;
        string temporary = output + ".elcl-export-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var archive = new ZipArchive(target, ZipArchiveMode.Create, true))
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(file.Path) & FileAttributes.ReparsePoint) != 0) throw new IOException("启动器分发文件不允许链接: " + file.Path);
                    await using var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    await using var destination = archive.CreateEntry(file.Entry, file.Path == pack ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open();
                    byte[] buffer = new byte[65536];
                    int count;
                    while ((count = await input.ReadAsync(buffer, token)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, count), token);
                        completed += count;
                        progress.Report(completed * 100d / total);
                    }
                }

            token.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static IReadOnlyList<string> ProgramFiles()
    {
        string root = AppContext.BaseDirectory;
        string executable = Path.Combine(root, "ErhioLab.Craft.Launcher.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException(L("packExport.launcherMissing"), executable);
        List<string> files = [executable];
        files.AddRange(Directory.EnumerateFiles(root, "*.dll"));
        foreach (string name in new[] { "ErhioLab.Craft.Launcher.deps.json", "ErhioLab.Craft.Launcher.runtimeconfig.json" })
            if (File.Exists(Path.Combine(root, name)))
                files.Add(Path.Combine(root, name));
        foreach (string name in new[] { "runtimes", "ThirdParty" })
        {
            string directory = Path.Combine(root, name);
            if (Directory.Exists(directory))
                files.AddRange(Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }));
        }

        return files;
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
