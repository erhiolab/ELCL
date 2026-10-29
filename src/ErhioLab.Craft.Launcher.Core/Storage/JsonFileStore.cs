using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Storage;

/// <summary>在调用方指定的位置读写任意引用类型的 JSON 文件.</summary>
/// <typeparam name="T">由调用方定义的配置或数据类型.</typeparam>
public sealed class JsonFileStore<T> where T : class
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>创建 JSON 文件存储, 不决定路径和数据结构.</summary>
    /// <param name="filePath">JSON 文件路径.</param>
    /// <param name="jsonOptions">调用方指定的序列化规则, 为空时使用缩进格式.</param>
    public JsonFileStore(string filePath, JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _jsonOptions = jsonOptions is null
            ? new JsonSerializerOptions { WriteIndented = true }
            : new JsonSerializerOptions(jsonOptions);
    }

    /// <summary>读取 JSON, 文件不存在或内容为 null 时返回调用方提供的默认对象.</summary>
    /// <param name="defaultValue">文件不存在时使用的对象.</param>
    /// <returns>读取到的对象或默认对象.</returns>
    public T Load(T defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        if (!File.Exists(_filePath))
        {
            return defaultValue;
        }

        string json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? defaultValue;
    }

    /// <summary>通过临时文件替换正式文件, 避免写入中断留下不完整的 JSON.</summary>
    /// <param name="value">要保存的对象.</param>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, _jsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
