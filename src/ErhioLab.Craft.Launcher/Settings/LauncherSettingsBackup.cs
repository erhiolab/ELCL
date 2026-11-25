using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ErhioLab.Craft.Launcher.Settings;

internal sealed record LauncherSettingsBackup([property: JsonRequired] LauncherSettings Settings)
{
    internal static JsonSerializerOptions CreateOptions(JsonSerializerOptions defaults) => new(defaults)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                type =>
                {
                    if (type.Type != typeof(LauncherSettings)) return;
                    for (int i = type.Properties.Count - 1; i >= 0; i--)
                    {
                        if (type.Properties[i].Name is nameof(LauncherSettings.GameFolders) or nameof(LauncherSettings.SelectedGameFolderPath)
                            or nameof(LauncherSettings.SelectedVersionPath) or nameof(LauncherSettings.SelectedVersionId)
                            or nameof(LauncherSettings.DailyLuckDate)
                            or nameof(LauncherSettings.DailyLuckMessageKey) or nameof(LauncherSettings.ZeroDownloadThreadsEggShown))
                            type.Properties.RemoveAt(i);
                    }
                }
            }
        }
    };

    internal LauncherSettings ApplyTo(LauncherSettings local) => (Settings with
    {
        GameFolders = local.GameFolders,
        SelectedGameFolderPath = local.SelectedGameFolderPath,
        SelectedVersionPath = local.SelectedVersionPath,
        SelectedVersionId = local.SelectedVersionId,
        DailyLuckDate = local.DailyLuckDate,
        DailyLuckMessageKey = local.DailyLuckMessageKey,
        ZeroDownloadThreadsEggShown = local.ZeroDownloadThreadsEggShown
    }).Normalize();
}
