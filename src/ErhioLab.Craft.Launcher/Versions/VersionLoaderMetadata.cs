using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Versions;

internal static class VersionLoaderMetadata
{
    private const string LoadersKey = "elclLoaders";
    private const string ManagedModKey = "elclLoaderMod";

    internal static MinecraftLoaderSelection[] ReadSelections(JsonObject metadata, LocalMinecraftVersion version)
    {
        if (metadata[LoadersKey] is JsonArray list)
        {
            try
            {
                var recorded = list.Deserialize<MinecraftLoaderSelection[]>();
                if (
                    recorded is not null &&
                    recorded.All(item => item is { Version: not null } && Enum.IsDefined(item.Kind) && !string.IsNullOrWhiteSpace(item.Version.Version)) &&
                    recorded.DistinctBy(item => item.Kind).Count() == recorded.Length
                )
                    return recorded;
            }
            catch (JsonException)
            {
                /* 外部版本可能包含不同的自定义字段. */
            }
        }

        if (Enum.TryParse(version.Loader, out MinecraftLoaderKind kind) && Enum.IsDefined(kind))
            return [new(kind, new MinecraftLoaderVersion(version.LoaderVersion ?? "", false))];
        return [];
    }

    internal static async Task StampAsync(string path, IReadOnlyList<MinecraftLoaderSelection> loaders,
        string? managedModPath, CancellationToken token)
    {
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(path, token).ConfigureAwait(false))!.AsObject();
        metadata[LoadersKey] = JsonSerializer.SerializeToNode(loaders);
        metadata.Remove(ManagedModKey);
        if (managedModPath is not null)
        {
            await using var input = File.OpenRead(managedModPath);
            metadata[ManagedModKey] = new JsonObject
            {
                ["file"] = Path.GetFileName(managedModPath),
                ["sha256"] = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false))
            };
        }

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static (string Name, string Hash)? ReadManagedMod(JsonObject metadata)
    {
        if (
            metadata[ManagedModKey] is not JsonObject mod ||
            ReadString(mod["file"]) is not { } name ||
            !(name.StartsWith("OptiFine_", StringComparison.Ordinal) || name.StartsWith("preview_OptiFine_", StringComparison.Ordinal)) ||
            !name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            ReadString(mod["sha256"]) is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigit)
        ) return null;
        return (name, hash);
    }

    internal static string? ReadString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
