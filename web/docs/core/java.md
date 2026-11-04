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

## 找不到时：全盘搜索

`FindExhaustiveAsync` 是最后的手段。默认会递归遍历所有就绪的本地磁盘（Windows 的固定盘和可移动盘；其他系统从根目录开始），寻找名为 `java.exe` 或 `java` 的文件，然后逐个运行版本命令验证。它没有普通搜索的目录数量和候选数量上限。目录链接会跳过，无法读取的目录也会跳过；所以“全盘”仍不能保证找出无权限访问的 Java。

```csharp
using ErhioLab.Craft.Launcher.Core.Java;

using CancellationTokenSource stop = new();
var progress = new Progress<JavaDiscoveryProgress>(state =>
    Console.WriteLine($"已检查 {state.DirectoriesScanned} 个目录，找到 {state.CandidatesFound} 个候选"));

// 可以把 stop.Token 接到界面的“取消”按钮。
IReadOnlyList<JavaInstallation> all = await JavaDiscovery.FindExhaustiveAsync(
    progress: progress,
    cancellationToken: stop.Token);
```

已经知道大概位置时，强烈建议限定范围：

```csharp
var found = await JavaDiscovery.FindExhaustiveAsync(
    roots: [@"D:\Games", @"E:\Tools"],
    cancellationToken: stop.Token);
```

::: danger 这不是普通刷新
全盘搜索可能读取数十万甚至数百万个目录，让机械硬盘长时间忙碌，也会拖慢其他程序。可移动盘同样会被扫描；其他系统从 `/` 开始时，还可能碰到挂载的文件系统。**更重要的是，验证会实际运行每一个匹配名称的可执行文件**：如果磁盘上有伪装成 `java.exe` 的恶意程序，它也可能被执行。不要在应用启动、后台定时任务或用户没有明确选择时调用它。提供取消按钮，优先让用户选择可信的搜索目录；对不可信磁盘不要使用此方法。
:::

进度只报告已扫描目录和候选数量；事先不知道目录总数，因此没有可靠的百分比。扫描完成后还需要时间逐个验证候选文件。取消时会抛出 `OperationCanceledException`，调用方应处理它。
