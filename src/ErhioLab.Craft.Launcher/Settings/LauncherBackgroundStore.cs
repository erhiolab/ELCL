using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Settings;

internal sealed record LauncherBackgroundData(LauncherBackgroundSettings? Options = null, BackgroundSlideshowPosition? Position = null);

internal sealed class LauncherBackgroundStore(string path)
{
    private readonly LauncherDataFile<LauncherBackgroundData> _file = new(path, LauncherDataKind.Background, new JsonSerializerOptions
    {
        Converters = { new JsonStringEnumConverter<BackgroundFit>(), new JsonStringEnumConverter<BackgroundPlaybackMode>() }
    });

    private readonly object _gate = new();
    private volatile LauncherBackgroundData _data = new(new LauncherBackgroundSettings(), new BackgroundSlideshowPosition());
    private bool _canWrite = true;

    internal LauncherBackgroundSettings Options => _data.Options!;
    internal BackgroundSlideshowPosition Position => _data.Position!;

    internal void Load()
    {
        try
        {
            var data = _file.Load(_data);
            _data = new(data.Options?.Normalize() ?? new LauncherBackgroundSettings(), data.Position ?? new BackgroundSlideshowPosition());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
            _canWrite = false;
            App.Logger.Error("Background", "读取背景配置失败, 本次使用默认值并保留原文件.", error);
        }
    }

    internal void SaveOptions(LauncherBackgroundSettings options)
    {
        lock (_gate)
        {
            var normalized = options.Normalize();
            if (normalized != _data.Options) Save(_data with { Options = normalized });
        }
    }

    internal void SavePosition(BackgroundSlideshowPosition position)
    {
        lock (_gate)
        {
            if (position != _data.Position) Save(_data with { Position = position });
        }
    }

    private void Save(LauncherBackgroundData data)
    {
        if (!_canWrite) throw new IOException(LocalizationManager.Get("settings.localFileUnreadable"));
        _file.Save(data);
        _data = data;
    }
}
