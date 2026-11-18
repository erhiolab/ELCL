using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ErhioLab.Craft.Launcher.Core.Modpacks;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Versions;

internal sealed class ModpackExportNode : INotifyPropertyChanged
{
    private bool _selected;
    private bool _expanded;
    private string _filter = "";
    internal ModpackExportNode? Parent { get; init; }
    internal ModpackExportFile? File { get; init; }
    internal ModpackExportCategory? Category { get; init; }
    internal Action? SelectionChanged { get; set; }
    public string Path { get; init; } = "";
    public string Name => Category is { } category ? LocalizationManager.Get("packExport.category." + category) : Path.Split('/')[^1];
    public ObservableCollection<ModpackExportNode> Children { get; } = [];
    public IEnumerable<ModpackExportNode> VisibleChildren => Children.Where(child => child.Matches);
    public bool Matches => _filter.Length == 0 || Path.Contains(_filter, StringComparison.OrdinalIgnoreCase) || Children.Any(child => child.Matches);
    public bool CanSelect => File?.CanSelect ?? Children.Any(child => child.CanSelect);

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            Changed();
        }
    }

    public string Summary
    {
        get
        {
            string size = DataSizeFormatter.Format(Files().Sum(file => file.Size), App.Settings.MemoryUnit, "0.##", minimumPower: 0);
            return File is not null ? size : string.Format(LocalizationManager.Get("packExport.nodeSummary"), Files().Count(), size);
        }
    }

    public bool? IsChecked
    {
        get
        {
            if (File is not null) return _selected;
            bool?[] values = Children.Where(child => child.CanSelect).Select(child => child.IsChecked).ToArray();
            return values.Length == 0 ? false : values.All(value => value == true) ? true : values.All(value => value == false) ? false : null;
        }
        set
        {
            if (value is not { } selected) return;
            SetSelected(selected);
            for (var parent = Parent; parent is not null; parent = parent.Parent) parent.Changed();
            Root().SelectionChanged?.Invoke();
        }
    }

    private ModpackExportNode Root() => Parent?.Root() ?? this;

    private void SetSelected(bool value)
    {
        if (File is { CanSelect: true }) _selected = value;
        foreach (var child in Children) child.SetSelected(value);
        Changed(nameof(IsChecked));
    }

    internal IEnumerable<ModpackExportFile> Files() => File is not null ? [File] : Children.SelectMany(child => child.Files());
    internal IEnumerable<string> SelectedPaths() => File is { CanSelect: true } && _selected ? [File.Path] : Children.SelectMany(child => child.SelectedPaths());

    internal void ApplySelection(ISet<string> paths)
    {
        if (File is not null) _selected = File.CanSelect && paths.Contains(File.Path);
        foreach (var child in Children) child.ApplySelection(paths);
        Changed(nameof(IsChecked));
    }

    internal void Filter(string text)
    {
        _filter = text;
        foreach (var child in Children) child.Filter(text);
        if (text.Length > 0) IsExpanded = true;
        Changed(nameof(VisibleChildren));
    }

    internal static IReadOnlyList<ModpackExportNode> Create(ModpackExportScan scan, Action changed)
    {
        List<ModpackExportNode> roots = [];
        foreach (var group in scan.Files.GroupBy(file => file.Category).OrderBy(group => group.Key))
        {
            string? prefix = group.Key switch
            {
                ModpackExportCategory.Mods => "mods", ModpackExportCategory.ResourcePacks => "resourcepacks",
                ModpackExportCategory.ShaderPacks => "shaderpacks", ModpackExportCategory.Saves => "saves",
                ModpackExportCategory.Screenshots => "screenshots", _ => null
            };
            bool skipPrefix = prefix is not null && group.All(file => file.Path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
            var root = new ModpackExportNode { Path = skipPrefix ? prefix! : "", Category = group.Key, SelectionChanged = changed };
            Dictionary<string, ModpackExportNode> nodes = new(StringComparer.OrdinalIgnoreCase);
            foreach (var file in group)
            {
                ModpackExportNode parent = root;
                string relative = skipPrefix ? prefix! : "";
                var parts = file.Path.Split('/');
                for (int index = skipPrefix ? 1 : 0; index < parts.Length; index++)
                {
                    relative = relative.Length == 0 ? parts[index] : relative + "/" + parts[index];
                    if (!nodes.TryGetValue(relative, out var node))
                    {
                        node = new ModpackExportNode { Path = relative, Parent = parent, File = index == parts.Length - 1 ? file : null, _selected = file.DefaultSelected };
                        nodes.Add(relative, node);
                        parent.Children.Add(node);
                    }

                    parent = node;
                }
            }

            roots.Add(root);
        }

        return roots;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

internal sealed record ModpackExportProfile(int FormatVersion, string Name, string Version, string[] SelectedPaths, bool IncludeLauncher, bool BundleResources, bool OnlyModrinth);
