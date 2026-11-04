# 查找 Java

`JavaDiscovery` 会从环境变量和常见安装位置寻找 Java，然后运行候选程序确认它是否真的可用。它不会从头到尾扫描每个磁盘。

```csharp
using ErhioLab.Craft.Launcher.Core.Java;

var found = await JavaDiscovery.FindAsync();
foreach (var java in found)
    Console.WriteLine($"{java.Version} · {java.Architecture} · {java.Path}");
```

如果你知道某个额外目录，可以传给 `additionalRoots`：

```csharp
var extra = await JavaDiscovery.FindAsync(
    additionalRoots: [@"D:\MyJdks"],
    cancellationToken: cancellationToken);
```

用户手动选择 `java.exe` 后，用 `JavaDiscovery.ProbeAsync(path)` 验证文件。返回 `null` 表示无法识别或运行。列表排序、兼容性选择、用户主动移除的记录，都由你的程序保存。Windows 注册表等平台专属线索也可以由你的程序读取后传入 `additionalRoots`。
