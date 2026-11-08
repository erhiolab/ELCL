# 准备启动 Minecraft

启动一个已经安装的版本，需要先读取版本 JSON，确定依赖和 Java 要求，再生成 Java 的参数。ELCLC 提供这几步的处理方法，调用方负责账户、下载任务、启动进程和界面反馈。

## 读取版本与继承关系

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

MinecraftLaunchVersion version = await MinecraftLaunchVersion.LoadAsync(
    gameDirectory,
    Path.Combine(gameDirectory, "versions", "1.21.1", "1.21.1.json"),
    cancellationToken: cancellationToken);
```

`LoadAsync` 会读取 `inheritsFrom` 指向的父版本，合并支持库和 JVM、游戏参数。子版本覆盖同一 Maven 组件的父版本依赖；参数按父版本在前、子版本在后的顺序合并。重复的游戏参数通常由后者覆盖，`--tweakClass` 可以保留多项。循环继承、超过 32 层的继承和越界路径会被拒绝。读取过程不会改写原有 JSON。

父版本缺失时，可以提供获取方法。这个方法只需返回 JSON 文本，如何缓存或下载由调用方决定：

```csharp
var version = await MinecraftLaunchVersion.LoadAsync(
    gameDirectory, versionJsonPath,
    fetchParent: (id, token) => ReadOrDownloadParentJsonAsync(id, token),
    cancellationToken: cancellationToken);
```

`JavaMajorVersion` 优先使用版本 JSON 的 `javaVersion.majorVersion`。旧 JSON 没有这个字段时，按可识别的正式版本号推算：1.16 及以前为 Java 8，1.17 为 Java 16，1.18～1.20.4 为 Java 17，1.20.5 起为 Java 21，26.x 为 Java 25。无法识别的老版本标识回退到 Java 8；调用方如有更准确的运行时要求，可以调整返回的记录。

`IsJavaCompatible` 对旧 Java 保守匹配：Java 8、17 使用对应主版本，Java 16 也接受 17；要求 Java 21 及以上时接受不低于要求的版本。具体模组仍可能限制 Java 的更新版本，不能只靠这个方法保证每个整合包都兼容。

## 校验并补全文件

```csharp
MinecraftLaunchFilePlan files = MinecraftLaunchFiles.Plan(
    version, MinecraftManifestSource.Official);

IReadOnlyList<DownloadFileRequest> missing =
    await MinecraftVanillaDownloadPlanner.MissingFilesAsync(
        files.Files, cancellationToken);
```

`Plan` 按目标系统和架构筛选支持库，并返回游戏 JAR、支持库、资源索引、日志配置、类路径和需要解压的本地库。默认目标环境是当前系统，也可传入 `MinecraftLaunchEnvironment`；例如使用 32 位 Java 时，应将目标架构设为 `x86`。

文件保存在标准共享目录中：`libraries/` 和 `assets/` 不会为每个版本重复创建。能取得大小和 SHA-1 的文件会据此校验，校验不会只比较大小。缺失或损坏的文件可交给 [下载器](downloader.md)，下载地址会按所选官方／镜像优先级排列。

安装器生成的本地库、显式 `jar` 引用等可能没有下载地址。这类请求的 `Sources` 为空；若文件不存在，调用方应要求重新安装对应版本或加载器，而不是拿原版 JAR 覆盖它。

资源索引下载并校验后，再从中收集资源对象：

```csharp
string indexJson = await File.ReadAllTextAsync(
    files.AssetIndex.DestinationPath, cancellationToken);
var assets = MinecraftVanillaDownloadPlanner.ParseAssets(
    indexJson, gameDirectory, MinecraftManifestSource.Official);
var missingAssets = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(
    assets, cancellationToken);
// 将 missingAssets 下载完成后继续。
```

旧版索引可能要求额外的目录映射。资源对象准备好后调用：

```csharp
await MinecraftLaunchFiles.MaterializeLegacyAssetsAsync(
    indexJson, gameDirectory, workingDirectory,
    files.AssetIndexId, cancellationToken);
```

它只在索引要求 `virtual` 或 `map_to_resources` 时复制资源。路径会限制在目标目录内，不修改对象缓存的内容。

## 解压本地库

为每次游戏进程提供单独的本地库目录，避免不同版本、架构或同时运行的进程互相覆盖：

```csharp
string nativesDirectory = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N"), "natives");
await MinecraftLaunchFiles.ExtractNativesAsync(
    files, nativesDirectory, cancellationToken);
```

解压会遵守版本中的排除列表，跳过 `META-INF/` 并检查 ZIP 路径。进程退出后，由调用方清理本次运行创建的目录；不要在游戏仍运行时删除它。

## 生成启动参数

先完成账户验证。Microsoft 账户可以使用 [账户 API](microsoft-auth.md) 检查授权或刷新令牌；离线账户的 UUID 可通过 [OfflinePlayerId](offline-accounts.md) 计算。ELCLC 不会替你选择账户或读取令牌文件。

```csharp
using System.Diagnostics;

var options = new MinecraftLaunchOptions
{
    Java = selectedJava,
    Context = new MinecraftLaunchPlaceholderContext
    {
        LauncherName = "My Launcher",
        LauncherVersion = "1.0",
        VersionName = "1.21.1",
        GameVersion = version.GameVersion,
        PlayerName = session.UserName,
        PlayerUuid = session.Uuid
    },
    WorkingDirectory = version.VersionDirectory,
    NativesDirectory = nativesDirectory,
    IsMicrosoftAccount = true,
    AccessToken = session.MinecraftAccessToken,
    ClientId = microsoftClientId,
    MemoryMiB = 4096,
    GarbageCollector = MinecraftGarbageCollector.StandardG1,
    WindowWidth = 1280,
    WindowHeight = 720,
    JvmArguments = "-XX:-OmitStackTraceInFastThrow",
    GameArguments = ""
};

ProcessStartInfo start = MinecraftLaunchBuilder.CreateStartInfo(version, files, options);
using Process process = Process.Start(start)!;
Task readOutput = ReadGameOutputAsync(process.StandardOutput);
Task readError = ReadGameOutputAsync(process.StandardError);
await Task.WhenAll(readOutput, readError, process.WaitForExitAsync());
```

`CreateStartInfo` 只生成 `ProcessStartInfo`，不执行命令。它支持新版 `arguments.jvm/game`、带规则的条件参数和旧版 `minecraftArguments`。每个参数分别放进 `ArgumentList`，含空格的路径、用户名不会被重新拆开。标准输出和错误输出都已重定向，调用方必须持续读取，避免缓冲区填满后阻塞游戏。

`WorkingDirectory` 决定进程的工作目录，以及 `${game_directory}`、`{version_indie}`、`{verindie}` 的值。上例传入版本目录，启用版本隔离；`version.GameDirectory` 仍是共享的 Minecraft 根目录，供 `libraries` 和 `assets` 使用。Core 不决定是否隔离，由调用方传入路径。旧版 `resources/` 映射应使用相同的工作目录。

`JvmArguments`、`GameArguments` 使用双引号分组；可用 `SplitArguments` 单独检查语法。自定义参数先拆分，再替换标记，防止替换出来的空格改变参数边界。未配对的引号或未解析的标记会报错。

内存至少为 128 MiB。内存设置会替换已有的 `-Xmx` 和 `-Xms`；GC 预设会清除冲突的 GC 参数。`Custom` 保留调用方自己的 GC 配置。ZGC 根据 Java 版本和 Windows 支持情况回退到 G1；Java 21、22 使用分代 ZGC 时增加 `-XX:+ZGenerational`，23 及以后不再增加这个标记。

兼容补丁路径通过可选的 `JavaWrapperPath`、`LwjglAgentPath` 传入。Core 不包含或下载这些补丁，也不决定何时启用。进程优先级、窗口标题、最大化和显卡偏好属于平台层，由调用方应用。

## 启动前命令和日志

`MinecraftLaunchBuilder.CreateContext` 可以生成带有这次实际 Java、目录、资源索引和类路径的替换上下文，用于标题或启动前命令。它包含访问令牌等原生参数，**不要序列化上下文或把完整启动参数写进日志**。调用方应对游戏输出中的令牌脱敏。

Core 的替换器不会执行命令或自动添加引号。若允许用户填写 shell 命令，必须由调用方明确选择 shell、工作目录、等待和取消行为。不要直接将全部游戏参数拼进 shell 命令。

这些 API 准备的是已经安装好的版本，支持符合 Mojang JSON 结构的原版及已安装加载器元数据；不会执行 Forge 安装器、处理器或将 Prism 的组件清单转换成 Mojang JSON。
