# 准备启动 Minecraft

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

核心读取版本、准备依赖和参数，最后返回 `ProcessStartInfo`。是否允许启动、如何登录、执行用户命令和管理游戏进程，由应用决定。

## 示例准备

示例按下面各节顺序执行，使用已有的 `1.21.1` 安装和离线账户信息。它会按需补齐缺失文件，最后只生成启动信息，不自动运行游戏。

```csharp
using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
await using var downloads = new DownloadManager(http);
CancellationToken cancellationToken = default;
string gameDirectory = Path.GetFullPath(".minecraft");
string versionDirectory = Path.Combine(gameDirectory, "versions", "1.21.1");
string workingDirectory = versionDirectory;
string nativesDirectory = Path.GetFullPath("work/run-001/natives");
```

## `MinecraftLaunchVersion.LoadAsync` {#minecraftlaunchversion-loadasync}

```csharp
var version = await MinecraftLaunchVersion.LoadAsync(gameDirectory,
    Path.Combine(versionDirectory, "1.21.1.json"),
    cancellationToken: cancellationToken);
```

读取 JSON 和本地父版本，合并库与参数，不修改原文件。子版本覆盖相同 Maven 组件，参数保留父在前、子在后的顺序，兼容旧 `minecraftArguments` 与新 `arguments.game` 混用。

| 参数 | 含义 |
| --- | --- |
| `gameDirectory` | 共享游戏根目录。 |
| `versionJsonPath` | 本次启动的元数据文件。 |
| `fetchParent` | 可选，缺少本地父版本时返回 JSON 文本的异步回调。 |
| `cancellationToken` | 可选，控制读取与父版本获取。 |

未提供回调且父版本缺失时抛出 `FileNotFoundException`。回调签名为 `Func<string, CancellationToken, Task<string>>`，缓存和下载由调用方决定。

循环继承、过深继承和不合法父路径会拒绝。返回的 `Json` 已合并，`ClientJarPath` 指向实际客户端，`JavaMajorVersion` 表示要求的主版本。

## `MinecraftLaunchVersion.IsJavaCompatible` {#minecraftlaunchversion-isjavacompatible}

根据元数据要求判断一个 Java 主版本是否在当前保守规则内。

```csharp
var java = (await JavaDiscovery.FindAsync(cancellationToken: cancellationToken))
    .FirstOrDefault(value => version.IsJavaCompatible(value.MajorVersion))
    ?? throw new InvalidOperationException("没有兼容 Java。");
```

默认要求主版本一致；Java 16 要求接受 17，要求为 21 及以上时接受不低于它的版本。元数据缺少要求时按游戏版本推断。

这是核心兼容规则，不是任意旧模组对新版 Java 的兼容保证。还应使用 `ProbeAsync` 确认运行时可以执行。

## `MinecraftLaunchFiles.Plan` {#minecraftlaunchfiles-plan}

从合并后的元数据生成启动文件计划、类路径、本地库和日志配置。

```csharp
var files = MinecraftLaunchFiles.Plan(version, MinecraftManifestSource.Official);
var missing = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(
    files.Files, cancellationToken);
if (missing.Any(file => file.Sources.Count == 0))
    throw new IOException("有安装器产物缺失，需要重新安装加载器。");
if (missing.Count > 0)
    await downloads.Enqueue(new DownloadTaskRequest("启动依赖", missing),
        new DownloadOptions(8), cancellationToken: cancellationToken).Completion;
```

`environment` 可指定目标操作系统、架构和系统版本，默认当前环境。文件规则与 native classifier 应与本次 Java 架构一致。

`Files` 不包含资源索引里的全部对象。下载索引后，还要用[ParseAssets](vanilla-installation.md)得到资源请求并校验下载：

```csharp
string indexJson = await File.ReadAllTextAsync(files.AssetIndex.DestinationPath, cancellationToken);
var assets = MinecraftVanillaDownloadPlanner.ParseAssets(
    indexJson, gameDirectory, MinecraftManifestSource.Official);
var missingAssets = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(assets, cancellationToken);
if (missingAssets.Count > 0)
    await downloads.Enqueue(new DownloadTaskRequest("游戏资源", missingAssets),
        new DownloadOptions(8), cancellationToken: cancellationToken).Completion;
```

来源列表为空的文件只能由安装器或调用方提供，不能交给通用下载器强行补全。

## `MinecraftLaunchFiles.ExtractNativesAsync` {#minecraftlaunchfiles-extractnativesasync}

```csharp
await MinecraftLaunchFiles.ExtractNativesAsync(files, nativesDirectory, cancellationToken);
```

从计划中的 native JAR 提取本地库，跳过 `META-INF` 和元数据声明的排除项，拒绝 ZIP 路径越界。

调用前先补齐 native JAR。每次运行使用独立目录，游戏退出后由调用方清理，避免多实例争用。

## `MinecraftLaunchFiles.MaterializeLegacyAssetsAsync` {#minecraftlaunchfiles-materializelegacyassetsasync}

```csharp
await MinecraftLaunchFiles.MaterializeLegacyAssetsAsync(
    indexJson, gameDirectory, workingDirectory, files.AssetIndexId, cancellationToken);
```

索引需要 `virtual` 或 `map_to_resources` 时，把已下载对象映射到 `assets/virtual/<索引>` 或工作目录下的 `resources`。不需要时不生成映射。

它不下载缺失对象。应先校验资源，再执行此方法。

## `MinecraftLaunchBuilder.CreateStartInfo` {#minecraftlaunchbuilder-createstartinfo}

```csharp
var options = new MinecraftLaunchOptions
{
    Java = java,
    Context = new MinecraftLaunchPlaceholderContext
    {
        LauncherName = "MyLauncher", LauncherVersion = "1.0",
        PlayerName = "Steve", PlayerUuid = OfflinePlayerId.FromName("Steve"),
        VersionName = Path.GetFileName(versionDirectory), GameVersion = version.GameVersion
    },
    WorkingDirectory = workingDirectory,
    NativesDirectory = nativesDirectory,
    AccessToken = "0",
    IsMicrosoftAccount = false,
    MemoryMiB = 2048
};
var start = MinecraftLaunchBuilder.CreateStartInfo(version, files, options);
```

返回已填充 `ArgumentList` 的启动信息，不调用 `Process.Start`。示例使用离线账户信息；使用 Microsoft 账户启动时，应传入有效的 Minecraft 会话，并将 `IsMicrosoftAccount` 设为 `true`。

| 选项 | 用途 |
| --- | --- |
| `Java`、`Context` | 运行时和玩家、版本、路径等上下文。 |
| `WorkingDirectory`、`NativesDirectory` | 游戏数据目录与本次本地库目录。 |
| `AccessToken`、`IsMicrosoftAccount`、`ClientId` | 身份参数，核心不替你检查会话或保存令牌。 |
| `MemoryMiB` | 最大堆内存，至少 128 MiB。 |
| `JvmArguments`、`GameArguments` | 用户附加参数，支持模板。 |
| `GarbageCollector` | ZGC、分代 ZGC、标准／调优 G1 或自定义。 |
| `Fullscreen`、`WindowWidth`、`WindowHeight` | 游戏参数支持的窗口选项。 |
| `JavaWrapperPath`、`LwjglAgentPath` | 调用方准备的兼容补丁路径，默认不传。 |
| `Environment` | 用于条件参数的目标环境。 |

内存参数由 `MemoryMiB` 统一生成；GC 按所选策略与 Java 能力处理。未知模板标记会抛出异常，避免生成无效命令。

返回值已重定向 stdout 和 stderr。真正运行它时要持续读取两个流，并自行处理取消、退出码、优先级和窗口；不要把含令牌的完整参数写入日志。

## `MinecraftLaunchBuilder.CreateContext` {#minecraftlaunchbuilder-createcontext}

```csharp
var context = MinecraftLaunchBuilder.CreateContext(version, files, options);
string title = MinecraftLaunchPlaceholders.Expand("{user} - {version}", context);
Console.WriteLine(title);
```

补齐类路径、资源索引、令牌、工作目录等 Minecraft 原生标记，适合生成标题或命令。

返回的上下文可能包含敏感值，不应整体序列化到日志。

## `MinecraftLaunchBuilder.SplitArguments` {#minecraftlaunchbuilder-splitarguments}

```csharp
var arguments = MinecraftLaunchBuilder.SplitArguments(
    "--server localhost --title \"My Minecraft\"");
```

按双引号和反斜杠规则拆分，返回字符串集合。它不是 Shell：不展开环境变量，不运行命令，不处理重定向管道。

模板本身的规则见[替换标记](launch-placeholders.md)。
