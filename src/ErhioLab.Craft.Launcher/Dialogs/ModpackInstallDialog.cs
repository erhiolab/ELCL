using System.IO;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Dialogs;

internal static class ModpackInstallDialog
{
    internal static string GetGameFolderDisplayName(string gameDirectory)
    {
        string path = Path.TrimEndingDirectorySeparator(gameDirectory);
        var folder = App.Settings.GameFolders.FirstOrDefault(item => string.Equals(Path.TrimEndingDirectorySeparator(item.Path), path, StringComparison.OrdinalIgnoreCase));
        if (folder is not null) return folder.DisplayName;
        if (App.Settings.SelectedGameFolderPath is null) return LocalizationManager.Get("folder.current");
        string name = Path.GetFileName(path);
        return name.Length == 0 ? LocalizationManager.Get("community.currentGameFolder") : name;
    }

    internal static Task<string?> PromptVersionNameAsync(string packName, string gameDirectory, string? minecraftVersion = null)
    {
        string versionLine = string.IsNullOrWhiteSpace(minecraftVersion) ? "" : string.Format(LocalizationManager.Get("modpack.inputGameVersion"), minecraftVersion) + "\n";
        string message = string.Format(LocalizationManager.Get("modpack.inputMessage"), packName, versionLine, GetGameFolderDisplayName(gameDirectory));
        string initial = string.Concat(packName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.');
        initial = initial[..Math.Min(initial.Length, 128)];
        return LauncherDialogs.InputAsync(LocalizationManager.Get("modpack.inputTitle"), message, initial, value =>
        {
            try
            {
                MinecraftVanillaDownloadPlanner.ValidateVersionName(value);
            }
            catch (ArgumentException)
            {
                return LocalizationManager.Get("versionSelector.invalidName");
            }

            string target = Path.Combine(gameDirectory, "versions", value);
            return Directory.Exists(target) || File.Exists(target) || App.Tasks.Tasks.Any(task => task.IsActive && string.Equals(task.TargetPath, target, StringComparison.OrdinalIgnoreCase)) ? LocalizationManager.Get("install.versionExists") : null;
        });
    }
}
