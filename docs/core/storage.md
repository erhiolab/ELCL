# JSON 文件存储

命名空间: `ErhioLab.Craft.Launcher.Core.Storage`

`JsonFileStore<T>` 在调用方指定的位置读写 JSON. `T` 是调用方定义的引用类型; Core 不认识任何启动器专属配置字段、默认值或校验规则.

```csharp
using System.Text.Json;
using ErhioLab.Craft.Launcher.Core.Storage;

JsonSerializerOptions options = new() { WriteIndented = true };
JsonFileStore<MyOptions> store = new("./data/options.json", options);
MyOptions optionsValue = store.Load(new MyOptions());
optionsValue.UseMirror = true;
store.Save(optionsValue);

public sealed class MyOptions
{
    public bool UseMirror { get; set; }
}
```

| API | 行为 |
| --- | --- |
| `new JsonFileStore<T>(filePath, jsonOptions)` | 将路径转为绝对路径; 未提供选项时默认缩进输出. |
| `FilePath` | 目标 JSON 文件的绝对路径. |
| `Load(defaultValue)` | 文件不存在或 JSON 为 `null` 时返回传入的默认对象. |
| `Save(value)` | 创建父目录, 先写同目录临时文件, 再替换正式文件; 短暂文件占用时最多重试 3 次. |

`Load` 不会自动创建文件, 也不会吞掉损坏 JSON、权限不足或其他 I/O 异常. `Save` 持续失败时会抛出异常. 调用方应决定读取失败后的策略, 并在保存前校验自己的数据. 临时文件只用于降低写入中断造成的不完整 JSON 风险; 不应把它当成跨进程事务机制.
