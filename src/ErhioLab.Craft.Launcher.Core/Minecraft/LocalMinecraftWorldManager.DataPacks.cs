namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public sealed record MinecraftWorldDataPackSelection(IReadOnlyList<string> Enabled, IReadOnlyList<string> Disabled);

public static partial class LocalMinecraftWorldManager
{
    public static Task<MinecraftWorldDataPackSelection> ReadDataPackSelectionAsync(string worldDirectory,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        string path = Path.Combine(Full(worldDirectory), "level.dat");
        EnsureNoLinks(path);
        return MinecraftWorldMetadata.OpenPackSelection(path, cancellationToken).Selection;
    }, cancellationToken);

    public static Task SetDataPackSelectionAsync(string worldDirectory, IEnumerable<string> fileNames, bool? enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        string[] names = fileNames.Distinct(StringComparer.Ordinal).ToArray();
        foreach (string name in names) ValidateName(name);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (names.Length == 0) return;
            string[] ids = names.Select(name => "file/" + name).ToArray();
            var edited = ids.ToHashSet(StringComparer.Ordinal);

            List<string> Rewrite(IReadOnlyList<string> values, bool isEnabledList)
            {
                if (enabled != isEnabledList) return values.Where(id => !edited.Contains(id)).ToList();
                var result = values.ToList();
                var present = values.ToHashSet(StringComparer.Ordinal);
                result.AddRange(ids.Where(present.Add));
                return result;
            }

            EditLevelDat(Full(worldDirectory), path =>
            {
                var document = MinecraftWorldMetadata.OpenPackSelection(path, cancellationToken);
                var newSelection = new MinecraftWorldDataPackSelection(Rewrite(document.Selection.Enabled, true), Rewrite(document.Selection.Disabled, false));
                return newSelection.Enabled.SequenceEqual(document.Selection.Enabled) && newSelection.Disabled.SequenceEqual(document.Selection.Disabled)
                    ? null : output => document.Write(output, newSelection);
            }, cancellationToken);
        }, cancellationToken);
    }

    public static Task<bool> ResetIconAsync(string worldDirectory, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        string world = Full(worldDirectory);
        string icon = Path.Combine(world, "icon.png");
        EnsureNoLinks(icon);
        if (!LooksLikeWorld(world)) throw new DirectoryNotFoundException("找不到指定的世界: " + world);
        using var worldLock = CheckWorldStopped(world);
        if (!File.Exists(icon)) return false;
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(icon);
        return true;
    }, cancellationToken);
}
