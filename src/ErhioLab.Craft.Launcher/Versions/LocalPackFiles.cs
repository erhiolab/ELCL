using System.IO;

namespace ErhioLab.Craft.Launcher.Versions;

internal static class LocalPackFiles
{
    internal static void ValidateDirectory(string directory)
    {
        string versionDirectory = Path.GetDirectoryName(directory)!;
        ValidateDirectoryIfPresent(versionDirectory);
        if (!Directory.Exists(versionDirectory)) throw new DirectoryNotFoundException(versionDirectory);
        ValidateDirectoryIfPresent(directory);
    }

    internal static FileAttributes? AttributesIfPresent(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal static void ValidateDirectoryIfPresent(string path)
    {
        if (AttributesIfPresent(path) is { } attributes && (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new IOException("Invalid directory: " + path);
    }

    internal static void ValidatePack(string directory, string path)
    {
        ValidateDirectory(directory);
        if (!string.Equals(path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Pack is outside its managed directory.");
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Pack is a symbolic link or junction.");
        if ((attributes & FileAttributes.Directory) == 0 && !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Pack is not a ZIP archive.");
    }

    internal static string ShaderConfigName(string packPath) => packPath + ".txt";

    internal static string GetShaderConfigPath(string directory, string packPath)
    {
        ValidatePack(directory, packPath);
        string config = ShaderConfigName(packPath);
        ValidateTextFile(config, mustExist: false);
        return config;
    }

    internal static void TransferShaderConfig(string directory, string packPath, string externalFile, bool importing, bool overwrite)
    {
        string config = GetShaderConfigPath(directory, packPath);
        string external = Path.GetFullPath(externalFile);
        string source = importing ? external : config;
        string destination = importing ? config : external;
        ValidateTextFile(source, mustExist: true);
        ValidateTextFile(destination, mustExist: false);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return;

        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".elcl-shader-config-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool created = false;
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                input.CopyTo(output);
            }

            ValidateTextFile(destination, mustExist: false);
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (created) File.Delete(temporary);
        }
    }

    private static void ValidateTextFile(string path, bool mustExist)
    {
        if (!Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Localization.LocalizationManager.Get("localPacks.shaderConfigTxtOnly"));
        if (AttributesIfPresent(path) is not { } attributes)
        {
            if (mustExist) throw new FileNotFoundException(Localization.LocalizationManager.Get("localPacks.shaderConfigMissing"), path);
            return;
        }

        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException(Localization.LocalizationManager.Get("localPacks.shaderConfigInvalid"));
    }

    internal static void Delete(string directory, string path, bool deleteShaderConfig)
    {
        string? shaderConfig = null;
        if (deleteShaderConfig) shaderConfig = GetShaderConfigPath(directory, path);
        else ValidatePack(directory, path);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
        if (shaderConfig is not null) File.Delete(shaderConfig);
    }
}
