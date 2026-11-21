using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>将已保存的隐藏选项与本次运行的临时恢复状态合并.</summary>
internal sealed class LauncherFeatureVisibility
{
    internal bool IsTemporarilyRevealed { get; private set; }
    internal event Action? Changed;

    internal bool IsHidden(LauncherHiddenFeatures feature) =>
        !IsTemporarilyRevealed && IsConfiguredHidden(feature);

    internal bool IsConfiguredHidden(LauncherHiddenFeatures feature) =>
        (App.Settings.HiddenFeatures & feature) != 0 || IsParentConfiguredHidden(feature);

    internal bool IsParentConfiguredHidden(LauncherHiddenFeatures feature) => (App.Settings.HiddenFeatures & ParentOf(feature)) != 0;

    private static LauncherHiddenFeatures ParentOf(LauncherHiddenFeatures feature) => feature switch
    {
        LauncherHiddenFeatures.LaunchSettings or LauncherHiddenFeatures.Personalization or LauncherHiddenFeatures.DownloadSettings
            or LauncherHiddenFeatures.OtherSettings or LauncherHiddenFeatures.Debug => LauncherHiddenFeatures.Settings,
        LauncherHiddenFeatures.About or LauncherHiddenFeatures.Toolbox or LauncherHiddenFeatures.Performance => LauncherHiddenFeatures.More,
        _ => LauncherHiddenFeatures.None
    };

    internal void ToggleTemporaryReveal()
    {
        IsTemporarilyRevealed = !IsTemporarilyRevealed;
        App.Logger.Information("Navigation", IsTemporarilyRevealed ? "已暂时显示隐藏功能." : "已恢复功能隐藏.");
        NotifyChanged();
        LauncherToasts.Information(LocalizationManager.Get(IsTemporarilyRevealed
            ? "featureVisibility.revealedToast"
            : "featureVisibility.restoredToast"));
    }

    internal void NotifyChanged() => Changed?.Invoke();
}
