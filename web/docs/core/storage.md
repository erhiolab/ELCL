# JSON 存储

命名空间：`ErhioLab.Craft.Launcher.Core.Storage`。

把应用自己定义的引用类型保存为 JSON。它不规定配置字段、默认值、存储目录或校验规则。

## 创建 `JsonFileStore<T>`

```csharp
using ErhioLab.Craft.Launcher.Core.Storage;

var store = new JsonFileStore<Dictionary<string, string>>("data/options.json");
Console.WriteLine(store.FilePath);
```

构造函数接受路径和可选的 `JsonSerializerOptions`，默认缩进输出。`FilePath` 为完整路径，构造不会立即读写文件。

`T` 可以替换成自己的类或 record，但必须是引用类型。

## `JsonFileStore.Load` {#jsonfilestore-load}

```csharp
var options = store.Load(new Dictionary<string, string>
{
    ["DownloadSource"] = "Official"
});
```

文件不存在，或内容为 JSON `null` 时，返回传入的默认对象。缺少某个字段不代表会用默认对象中的该字段补齐，实际反序列化遵循类型定义和序列化选项。

损坏 JSON、权限或 I/O 问题会抛出异常。建议保留原文件并告知用户，不要在捕获异常后无条件覆盖默认值。

## `JsonFileStore.Save` {#jsonfilestore-save}

```csharp
options["DownloadSource"] = "Mirror";
store.Save(options);
```

创建必要目录，先写同目录临时文件，再替换正式文件。目标短暂占用时有限重试，持续失败仍抛出异常。

保存会替换整份 JSON，不会自动保留模型之外的未知字段。如果需要保留扩展字段，可以在自己的类型中使用 `JsonExtensionData`。

这个类不合并并发修改，也不验证值的含义。官方 `launcher_profiles.json` 有专用的[安装档案 API](launcher-profiles.md)，不应直接用普通配置模型覆盖。
