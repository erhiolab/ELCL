# Java 发现

命名空间：`ErhioLab.Craft.Launcher.Core.Java`。

返回的 `JavaInstallation` 包含可执行路径、完整版本、主版本号和架构。所有发现结果都会实际运行 Java 验证，不只是判断文件名。

## `JavaDiscovery.FindAsync` {#javadiscovery-findasync}

普通查找，适合初始化列表或用户点击刷新。

```csharp
using ErhioLab.Craft.Launcher.Core.Java;

CancellationToken cancellationToken = default;
var installations = await JavaDiscovery.FindAsync(
    additionalRoots: new[] { @"D:\Tools\Java" },
    cancellationToken: cancellationToken);
foreach (var java in installations)
    Console.WriteLine($"{java.Version} · {java.Architecture} · {java.Path}");
```

`additionalRoots` 是额外的安装根目录，可省略。方法同时检查环境变量、系统登记和常见目录，使用有界扫描，不逐盘遍历。

结果去重后按 Java 主版本降序、路径排序，不代表你的应用必须采用这个优先级。用户排序和持久化由调用方处理。

## `JavaDiscovery.ProbeAsync` {#javadiscovery-probeasync}

验证单个 Java 可执行文件，适合手动添加。

```csharp
JavaInstallation? selected = await JavaDiscovery.ProbeAsync(
    @"C:\Program Files\Java\jdk-21\bin\java.exe", cancellationToken);
Console.WriteLine(selected is null ? "无法使用这个 Java。" : selected.Version);
```

路径必须指向当前平台的 `java.exe` 或 `java`，不是安装根目录，也不是 `javaw.exe`。

方法运行 `-XshowSettings:properties -version`，读取版本与架构，单次探测有超时。无法执行或不符合要求时返回 `null`；调用方主动取消仍会停止操作。

## `JavaDiscovery.FindExhaustiveAsync` {#javadiscovery-findexhaustiveasync}

暴力搜索，只有普通查找不足时再考虑使用。

```csharp
using var cancellation = new CancellationTokenSource();
var progress = new Progress<JavaDiscoveryProgress>(value =>
    Console.WriteLine($"已扫描 {value.DirectoriesScanned} 个目录，候选 {value.CandidatesFound} 个"));

var exhaustive = await JavaDiscovery.FindExhaustiveAsync(
    roots: new[] { @"D:\Tools" },
    progress: progress,
    cancellationToken: cancellation.Token);
```

| 参数 | 含义 |
| --- | --- |
| `roots` | 可选，递归搜索的根目录。省略时 Windows 搜索所有就绪的本地固定／可移动磁盘，其他系统从当前目录所在根开始。 |
| `progress` | 约每 256 个目录和扫描结束时报告一次，包含目录数、候选数和当前目录。 |
| `cancellationToken` | 同时控制遍历与后续候选验证。 |

::: warning 此方法开销很大
没有普通发现的目录数和深度上限。大量小文件、机械硬盘和多磁盘环境下，可能持续很久并产生明显磁盘 I/O。

找到候选文件后还会执行它来验证。应由用户明确发起，优先限制搜索根目录，提供取消入口；不要放进启动流程、定时任务或普通刷新按钮。
:::

无权限目录会跳过，遍历子目录时跳过链接与重解析点以避免循环。它仍不能保证发现无权限位置中的 Java。
