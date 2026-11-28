using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using ErhioLab.Craft.Launcher.Caching;
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Versions;

internal sealed class VersionLoaderEdit(
    string gameDirectory,
    LocalMinecraftVersion version,
    string originalJson,
    (string Name, string Hash)? managedMod)
{
    internal string StagingDirectory { get; } = Path.Combine(gameDirectory, ".elcl-modify-" + Guid.NewGuid().ToString("N"));
    internal string ClientPath => Path.Combine(version.FolderPath, Path.GetFileName(version.FolderPath) + ".jar");
    private bool _keepRecovery;

    internal static void ValidateTarget(string gameDirectory, LocalMinecraftVersion version)
    {
        if (
            !version.IsValid ||
            !Directory.Exists(version.FolderPath) ||
            !File.Exists(version.JsonPath) ||
            !VersionManagementService.SamePath(Path.GetDirectoryName(version.FolderPath), Path.Combine(gameDirectory, "versions")) ||
            !VersionManagementService.SamePath(Path.GetDirectoryName(version.JsonPath), version.FolderPath) ||
            CacheFileTree.IsLinked(version.JsonPath)
        )
            throw new IOException(LocalizationManager.Get("versionEdit.unsafeVersion"));
    }

    internal async Task ApplyAsync(string stagedJson, string stagedJar, CancellationToken token)
    {
        ValidateTarget(gameDirectory, version);
        if (CacheFileTree.IsLinked(ClientPath) || CacheFileTree.IsLinked(stagedJson) || CacheFileTree.IsLinked(stagedJar))
            throw new IOException(LocalizationManager.Get("versionEdit.unsafeVersion"));
        await Task.Run(() => VersionManagementService.RequireUnreferenced(gameDirectory, version), token);
        if (await File.ReadAllTextAsync(version.JsonPath, token) != originalJson)
            throw new IOException(LocalizationManager.Get("versionEdit.changed"));

        var staged = JsonNode.Parse(await File.ReadAllTextAsync(stagedJson, token))!.AsObject();
        var nextMod = VersionLoaderMetadata.ReadManagedMod(staged);
        string mods = Path.Combine(version.FolderPath, "mods");
        string? oldPath = managedMod is { } old ? Path.Combine(mods, old.Name) : null;
        string? nextPath = nextMod is { } next ? Path.Combine(mods, next.Name) : null;
        if ((oldPath is not null && CacheFileTree.IsLinked(oldPath)) || (nextPath is not null && CacheFileTree.IsLinked(nextPath)))
            throw new IOException(LocalizationManager.Get("versionEdit.unsafeVersion"));

        bool owned = oldPath is not null && File.Exists(oldPath) && managedMod is { } recorded
                     && string.Equals(await HashAsync(oldPath, token), recorded.Hash, StringComparison.OrdinalIgnoreCase);
        bool sameMod = owned && VersionManagementService.SamePath(oldPath, nextPath);
        if (nextPath is not null && (Directory.Exists(nextPath) || File.Exists(nextPath) && !sameMod))
            throw new IOException(string.Format(LocalizationManager.Get("versionEdit.modConflict"), Path.GetFileName(nextPath)));

        bool replaceJar = !File.Exists(ClientPath) || await HashAsync(ClientPath, token) != await HashAsync(stagedJar, token);
        token.ThrowIfCancellationRequested();

        // 提交阶段仅在同卷移动少量文件. JSON 最后替换, 失败时倒序回滚.
        Stack<Action> rollback = new();
        bool createdMods = false;
        try
        {
            if (replaceJar) Put(stagedJar, ClientPath);
            if (nextPath is not null && nextMod is { } item)
            {
                createdMods = !Directory.Exists(mods);
                Directory.CreateDirectory(mods);
                Put(Path.Combine(StagingDirectory, "mods", item.Name), nextPath);
            }

            if (owned && !sameMod && oldPath is not null)
            {
                string backup = BackupPath(oldPath);
                File.Move(oldPath, backup);
                rollback.Push(() => File.Move(backup, oldPath));
            }

            Put(stagedJson, version.JsonPath);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            List<Exception> errors = [failure];
            while (rollback.TryPop(out var undo))
            {
                try
                {
                    undo();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    errors.Add(error);
                }
            }

            try
            {
                if (createdMods && Directory.Exists(mods) && !Directory.EnumerateFileSystemEntries(mods).Any()) Directory.Delete(mods);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                errors.Add(error);
            }

            if (errors.Count == 1) throw;
            _keepRecovery = true;
            throw new AggregateException(string.Format(LocalizationManager.Get("versionEdit.rollbackFailed"), StagingDirectory), errors);
        }

        return;

        string BackupPath(string destination) => Path.Combine(StagingDirectory, Path.GetFileName(destination) + ".bak");

        void Put(string source, string destination)
        {
            if (File.Exists(destination))
            {
                string backup = BackupPath(destination);
                rollback.Push(() =>
                {
                    if (!File.Exists(backup)) return;
                    if (File.Exists(destination)) File.Replace(backup, destination, null);
                    else File.Move(backup, destination);
                });
                File.Replace(source, destination, backup);
            }
            else
            {
                File.Move(source, destination);
                rollback.Push(() => File.Delete(destination));
            }
        }
    }

    internal void Cleanup()
    {
        if (_keepRecovery || !Directory.Exists(StagingDirectory)) return;
        new CacheFileTree((path, error) => App.Logger.Error("Versions", $"无法清理版本修改临时文件: {path}.", error))
            .Sweep(StagingDirectory, _ => true);
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token));
    }
}
