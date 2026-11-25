namespace ErhioLab.Craft.Launcher.Settings;

internal sealed record BackgroundSlideshowPosition(string? LocalKey = null, string? FavoritesKey = null)
{
    internal string? GetKey(BackgroundCategory category) => category switch
    {
        BackgroundCategory.Local => LocalKey,
        BackgroundCategory.Favorites => FavoritesKey,
        _ => null
    };

    internal BackgroundSlideshowPosition WithKey(BackgroundCategory category, string key) => category switch
    {
        BackgroundCategory.Local => this with { LocalKey = key },
        BackgroundCategory.Favorites => this with { FavoritesKey = key },
        _ => this
    };
}
