using System.ComponentModel;

using ErhioLab.Craft.Launcher.Core.Mods;
using ErhioLab.Craft.Launcher.Views.Download;

namespace ErhioLab.Craft.Launcher.Mods;

public sealed class ModFileGroup : INotifyPropertyChanged
{
    private bool _isExpanded;
    private readonly ModFile[] _source;
    private IReadOnlyList<ModFileRow> _rows = [];
    private bool _rowsCreated;
    public string Loader { get; }
    public string GameVersion { get; }
    public string Key => Loader + ":" + GameVersion;
    public string Title => ModPresentation.LoaderName(Loader) + " " + GameVersion;
    public string CountText => string.Format(CommunityMods.L("fileCount"), _source.Length);
    public IReadOnlyList<ModFileRow> Files => _rows;
    internal IReadOnlyList<ModDependency> RequiredDependencies { get; }
    internal Task<IReadOnlyList<ModProjectRow>>? DependencyTask { get; set; }
    internal IReadOnlyList<ModProjectRow>? Dependencies { get; set; }
    internal string? DependencyError { get; set; }
    public System.Windows.Thickness HeaderMargin => new(0, 0, 6, IsExpanded ? 0 : 10);
    public System.Windows.CornerRadius HeaderCorners => IsExpanded ? new(8, 8, 0, 0) : new(8);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            if (value && !_rowsCreated)
            {
                _rows = _source.Select(file => new ModFileRow(file, file.GameVersions.Contains(GameVersion) ? GameVersion : null,
                    file.Loaders.Contains(Loader, StringComparer.OrdinalIgnoreCase) ? Loader : null)).ToArray();
                _rowsCreated = true;
                PropertyChanged?.Invoke(this, new(nameof(Files)));
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new(nameof(HeaderMargin)));
            PropertyChanged?.Invoke(this, new(nameof(HeaderCorners)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private ModFileGroup(string loader, string version, IEnumerable<ModFile> files)
    {
        Loader = loader;
        GameVersion = version;
        _source = files.DistinctBy(file => file.Id).OrderByDescending(file => file.Published).ToArray();
        RequiredDependencies = _source.SelectMany(file => file.RequiredDependencies)
            .DistinctBy(dependency => dependency.ProjectId is { Length: > 0 } id ? "project:" + id : "version:" + dependency.VersionId).ToArray();
    }

    internal static IReadOnlyList<ModFileGroup> Create(IEnumerable<ModFile> files, string? gameVersion, string? loader)
    {
        var entries = files.SelectMany(file =>
            (file.GameVersions.Count == 0 ? new[] { CommunityMods.L("unknown") } : file.GameVersions)
            .Where(version => gameVersion is null || version == gameVersion)
            .SelectMany(version => (file.Loaders.Count == 0 ? new[] { CommunityMods.L("unknown") } : file.Loaders)
                .Where(candidate => loader is null || candidate.Equals(loader, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => (Loader: candidate, Version: version, File: file))));
        return entries.GroupBy(entry => (entry.Loader, entry.Version))
            .OrderByDescending(group => group.Key.Version, ModPresentation.VersionOrder)
            .ThenBy(group => ModPresentation.LoaderOrder(group.Key.Loader))
            .Select(group => new ModFileGroup(group.Key.Loader, group.Key.Version, group.Select(entry => entry.File))).ToArray();
    }
}
