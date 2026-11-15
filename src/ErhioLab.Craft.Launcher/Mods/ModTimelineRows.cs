namespace ErhioLab.Craft.Launcher.Mods;

public sealed record ModSectionRow(string Text);

public sealed record ModDependencyRow(ModProjectRow Project)
{
    internal ModBrowserContext Context { get; init; } = new(null, null, null);
}

public sealed record ModGroupFooter(ModFileGroup Group);
