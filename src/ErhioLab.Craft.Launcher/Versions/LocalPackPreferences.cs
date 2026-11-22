using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Versions;

internal sealed partial class LocalPackPreferences
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string? _path;
    private readonly MinecraftPackKind _kind;
    private readonly Encoding _encoding;
    private readonly byte[] _original;
    private readonly List<string> _lines;
    private readonly string _newLine;
    private readonly string _separator;
    private readonly bool _optifine;
    private readonly bool _legacy;
    private readonly List<string> _resourceIds;
    private readonly List<string> _acceptedIncompatibleIds;
    private readonly string? _shader;
    private readonly bool _shaderEnabled;

    private LocalPackPreferences(string? path, MinecraftPackKind kind, string? gameVersion)
    {
        _path = path;
        _kind = kind;
        _optifine = Path.GetFileName(path) == "optionsshaders.txt";
        _separator = kind == MinecraftPackKind.ResourcePack ? ":" : "=";
        _encoding = kind == MinecraftPackKind.ResourcePack ? Utf8 : Encoding.Latin1;
        _original = path is null ? [] : ReadBytes(path);
        bool bom = kind == MinecraftPackKind.ResourcePack && _original.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        if (bom) _encoding = new UTF8Encoding(true, true);
        string text = _encoding.GetString(_original.AsSpan(bom ? 3 : 0));
        _newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') && !text.Contains('\n') ? "\r" : "\n";
        _lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).ToList();
        if (kind == MinecraftPackKind.ResourcePack)
        {
            _resourceIds = ReadIds("resourcePacks");
            _acceptedIncompatibleIds = ReadIds("incompatibleResourcePacks");
            _legacy = !_resourceIds.Any(id => id.StartsWith("file/", StringComparison.Ordinal))
                      && (Version.TryParse(gameVersion, out var version)
                          ? version.Major == 1 && version.Minor <= 12
                          : int.TryParse(Get("version"), out int dataVersion) && dataVersion <= 1343);
        }
        else
        {
            _resourceIds = [];
            _acceptedIncompatibleIds = [];
            _shader = Get("shaderPack");
            _shaderEnabled = !string.IsNullOrEmpty(_shader) && _shader is not ("OFF" or "(internal)") && (_optifine || Get("enableShaders") != "false");
        }
    }

    internal static LocalPackPreferences Load(string directory, MinecraftPackKind kind, string? gameVersion)
    {
        LocalPackFiles.ValidateDirectoryIfPresent(directory);
        if (kind == MinecraftPackKind.ResourcePack)
        {
            string path = Path.Combine(directory, "options.txt");
            return new(FileExists(path) ? path : null, kind, gameVersion);
        }

        string config = Path.Combine(directory, "config");
        LocalPackFiles.ValidateDirectoryIfPresent(config);
        string[] paths = [Path.Combine(config, "iris.properties"), Path.Combine(config, "oculus.properties"), Path.Combine(directory, "optionsshaders.txt")];
        string[] found = paths.Where(FileExists).ToArray();
        if (found.Length > 1) throw PreferenceError("multipleShaderSettings");
        return new(found.FirstOrDefault(), kind, gameVersion);
    }

    internal bool? IsEnabled(string fileName) => _path is null
        ? null
        : _kind == MinecraftPackKind.ResourcePack
            ? _resourceIds.Contains(ResourceId(fileName), StringComparer.Ordinal)
              || (!_legacy && IsZipName(fileName) && _resourceIds.Contains(fileName, StringComparer.Ordinal))
            : _shaderEnabled && _shader == fileName;

    internal void SetEnabled(IReadOnlyList<string> names, bool enabled, bool deleting = false)
    {
        if (names.Count == 0) return;
        if (_path is null)
        {
            if (deleting) return;
            throw PreferenceError(_kind == MinecraftPackKind.ResourcePack ? "missingResourceSettings" : "missingShaderSettings");
        }

        if (_kind == MinecraftPackKind.ResourcePack)
        {
            if (enabled)
            {
                var added = names.Where(name => IsEnabled(name) != true).Select(ResourceId).Distinct(StringComparer.Ordinal).ToArray();
                if (added.Length == 0) return;
                _resourceIds.AddRange(added);
            }
            else
            {
                var ids = names.Select(ResourceId).ToHashSet(StringComparer.Ordinal);
                if (!_legacy) ids.UnionWith(names.Where(IsZipName));
                if (_resourceIds.RemoveAll(ids.Contains) != 0) Set("resourcePacks", JsonSerializer.Serialize(_resourceIds));
                if (_acceptedIncompatibleIds.RemoveAll(ids.Contains) != 0)
                    Set("incompatibleResourcePacks", JsonSerializer.Serialize(_acceptedIncompatibleIds));
                Save();
                return;
            }

            Set("resourcePacks", JsonSerializer.Serialize(_resourceIds));
        }
        else if (enabled)
        {
            if (names.Count != 1) throw PreferenceError("oneShader");
            if (_shaderEnabled && names[0] == _shader) return;
            Set("shaderPack", EscapeProperty(names[0]));
            if (!_optifine) Set("enableShaders", "true");
        }
        else
        {
            if (_shader is null || !names.Contains(_shader, StringComparer.Ordinal) || (!_shaderEnabled && !deleting)) return;
            if (_optifine || deleting) Set("shaderPack", _optifine ? "OFF" : "");
            if (!_optifine) Set("enableShaders", "false");
        }

        Save();
    }

    private string ResourceId(string fileName) => _legacy ? fileName : "file/" + fileName;
    private static bool IsZipName(string fileName) => Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    private List<string> ReadIds(string key)
    {
        if (Get(key) is not { } value) return [];
        var ids = JsonSerializer.Deserialize<List<string>>(value);
        if (ids is null || ids.Any(id => id is null)) throw new InvalidDataException("Expected a list of pack IDs for " + key);
        return ids;
    }

    private string? Get(string key)
    {
        if (_kind == MinecraftPackKind.ResourcePack)
            return _lines.LastOrDefault(line => line.StartsWith(key + _separator, StringComparison.Ordinal))?[(key.Length + 1)..];
        string? value = null;
        foreach (var (_, raw) in Properties(key))
            value = PropertyEscape().Replace(raw, match => match.Groups[1].Success
                ? ((char)int.Parse(match.Groups[1].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString()
                : match.Groups[2].Value switch { "t" => "\t", "n" => "\n", "r" => "\r", "f" => "\f", var escaped => escaped });
        return value;
    }

    private void Set(string key, string value)
    {
        string line = key + _separator + value;
        var matches = _kind == MinecraftPackKind.ResourcePack
            ? Enumerable.Range(0, _lines.Count).Where(i => _lines[i].StartsWith(key + _separator, StringComparison.Ordinal)).ToArray()
            : Properties(key).Select(property => property.Index).ToArray();
        if (matches.Length == 0) _lines.Insert(_lines.Count > 0 && _lines[^1].Length == 0 ? _lines.Count - 1 : _lines.Count, line);
        else
            foreach (int i in matches)
                _lines[i] = line;
    }

    private IEnumerable<(int Index, string Value)> Properties(string key)
    {
        bool continued = false;
        for (int index = 0; index < _lines.Count; index++)
        {
            string line = _lines[index].TrimStart(' ', '\t', '\f');
            bool comment = !continued && line.Length > 0 && line[0] is '#' or '!';
            bool nextContinued = !comment && (line.Length - line.TrimEnd('\\').Length) % 2 != 0;
            if (!continued && !comment && line.StartsWith(key, StringComparison.Ordinal)
                && (line.Length == key.Length || line[key.Length] is '=' or ':' or ' ' or '\t' or '\f'))
            {
                if (nextContinued) throw new InvalidDataException("Multiline shader setting: " + key);
                string value = line[key.Length..].TrimStart(' ', '\t', '\f');
                if (value.Length > 0 && value[0] is '=' or ':') value = value[1..].TrimStart(' ', '\t', '\f');
                yield return (index, value);
            }

            continued = nextContinued;
        }
    }

    private static string EscapeProperty(string value)
    {
        var escaped = new StringBuilder();
        foreach (char character in value)
            if (character is < ' ' or > '~') escaped.Append(@"\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else
            {
                if (character is '\\' or ' ') escaped.Append('\\');
                escaped.Append(character);
            }

        return escaped.ToString();
    }

    private void Save()
    {
        string path = _path!;
        byte[] data = [.. _encoding.GetPreamble(), .. _encoding.GetBytes(string.Join(_newLine, _lines))];
        if (data.AsSpan().SequenceEqual(_original)) return;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(data);
            if (!FileExists(path) || !ReadBytes(path).AsSpan().SequenceEqual(_original)) throw PreferenceError("settingsChanged");
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static byte[] ReadBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Pack configuration is too large.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static bool FileExists(string path)
    {
        LocalPackFiles.ValidateDirectoryIfPresent(Path.GetDirectoryName(path)!);
        if (LocalPackFiles.AttributesIfPresent(path) is not { } attributes) return false;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw new IOException("Invalid configuration file: " + path);
        return true;
    }

    private static IOException PreferenceError(string key) => new(LocalizationManager.Get("localPacks." + key));

    [GeneratedRegex(@"\\(?:u([0-9A-Fa-f]{4})|(.))")]
    private static partial Regex PropertyEscape();
}
