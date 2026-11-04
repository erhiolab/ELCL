# 保存 JSON

`JsonFileStore<T>` 是一个小工具：把你定义的对象读写到指定 JSON 文件。它不知道你的配置有哪些字段，也不会替你验证设置值。

```csharp
using ErhioLab.Craft.Launcher.Core.Storage;

var store = new JsonFileStore<MyOptions>("./data/options.json");
var options = store.Load(new MyOptions());
options.UseMirror = true;
store.Save(options);

public sealed class MyOptions
{
    public bool UseMirror { get; set; }
}
```

文件不存在或内容是 JSON `null` 时，`Load` 返回你传入的默认对象。坏掉的 JSON、权限问题会抛出异常，让调用方决定怎么处理。

`Save` 先在同一目录写临时文件，再替换正式文件，避免写到一半留下不完整内容。目标文件短暂被占用时会重试；持续失败仍会抛出异常。
