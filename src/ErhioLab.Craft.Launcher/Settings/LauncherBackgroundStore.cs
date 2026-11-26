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

    private readonly Lock _gate = new();
    private LauncherBackgroundData _data = new(new LauncherBackgroundSettings(), new BackgroundSlideshowPosition());
    private bool _canWrite = true;

    // 读者获取完整快照, 不必等待磁盘写入. 加载与保存仍通过同一把锁串行执行.
    private LauncherBackgroundData Snapshot
    {
        get => Volatile.Read(ref _data);
        set => Volatile.Write(ref _data, value);
    }

    internal LauncherBackgroundSettings Options => Snapshot.Options!;
    internal BackgroundSlideshowPosition Position => Snapshot.Position!;

    internal void Load()
    {
        lock (_gate)
        {
            try
            {
                var data = _file.Load(Snapshot);
                Snapshot = new LauncherBackgroundData(data.Options?.Normalize() ?? new LauncherBackgroundSettings(), data.Position ?? new BackgroundSlideshowPosition());
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or SecurityException)
            {
                _canWrite = false;
                App.Logger.Error("Background", "读取背景配置失败, 本次使用默认值并保留原文件.", error);
            }
        }
    }

    internal void SaveOptions(LauncherBackgroundSettings options)
    {
        lock (_gate)
        {
            var normalized = options.Normalize();
            var current = Snapshot;
            if (normalized != current.Options) Save(current with { Options = normalized });
        }
    }

    internal void SavePosition(BackgroundSlideshowPosition position)
    {
        lock (_gate)
        {
            var current = Snapshot;
            if (position != current.Position) Save(current with { Position = position });
        }
    }

    private void Save(LauncherBackgroundData data)
    {
        if (!_canWrite) throw new IOException(LocalizationManager.Get("settings.localFileUnreadable"));
        _file.Save(data);
        Snapshot = data;
    }
}
