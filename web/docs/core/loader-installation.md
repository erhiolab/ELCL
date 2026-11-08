# 安装模组加载器

`MinecraftLoaderInstaller` 可以安装 Forge、NeoForge、Fabric、Quilt、LiteLoader 和 OptiFine。它处理加载器配置、依赖、客户端安装处理器和最终版本 JSON。原版游戏文件需要先准备好，下载队列、工作目录和 Java 进程由调用方管理。

## 安装顺序

1. 准备原版元数据、客户端 JAR、支持库和资源。JAR 可直接下载到最终安装目录，原版元数据放在调用方的缓存或工作目录中，不必额外创建一个原版版本。
2. 调用 `PrepareAsync` 获取安装计划。安装器 JAR 通过传入的下载回调获取，这一步也会解出安装包内自带的支持库。
3. 校验并下载计划中的 `Files`。
4. 调用 `RunProcessorsAsync` 执行客户端安装处理器。
5. 全部成功后调用 `WriteVersionAsync`，把原版和加载器合并成一份完整 JSON，并确保同目录内有与安装名一致的客户端 JAR。

```csharp
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
await using var queue = new DownloadManager(http);
var installer = new MinecraftLoaderInstaller(http);
var selections = new MinecraftLoaderSelection[]
{
    new(MinecraftLoaderKind.NeoForge, new MinecraftLoaderVersion("21.1.81", false))
};
string name = MinecraftLoaderInstaller.DefaultVersionName("1.21.1", selections);
// 1.21.1-NeoForge_21.1.81

async Task DownloadFiles(IReadOnlyList<DownloadFileRequest> files, CancellationToken token)
{
    var unique = files.DistinctBy(file => file.DestinationPath).ToArray();
    var missing = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(unique, token);
    if (missing.Count > 0)
        await queue.Enqueue(new DownloadTaskRequest(name, missing),
            new DownloadOptions(16), cancellationToken: token).Completion;
}

// 原版元数据已缓存到 originalJsonPath，客户端 JAR 已按安装名下载。
string clientJarPath = Path.Combine(gameDirectory, "versions", name, name + ".jar");
var plan = await installer.PrepareAsync("1.21.1", name, selections,
    gameDirectory, workDirectory, MinecraftManifestSource.Official,
    DownloadFiles, log: Console.WriteLine, cancellationToken: cancellationToken,
    baseVersionJsonPath: originalJsonPath, baseClientJarPath: clientJarPath);

await DownloadFiles(plan.Files, cancellationToken);
await installer.RunProcessorsAsync(plan, javaExecutable, RunJavaAsync,
    progress: new Progress<double>(percent => Console.WriteLine($"{percent:0}%")),
    cancellationToken: cancellationToken);
await MinecraftLoaderInstaller.WriteVersionAsync(plan, cancellationToken);
```

`RunJavaAsync` 是调用方提供的 `Task RunJavaAsync(ProcessStartInfo start, CancellationToken token)`。它需要等待进程退出、同时读取标准输出和错误输出，并在非零退出码时抛出异常。取消时应结束该进程及其子进程，只取消等待会让处理器继续修改文件。传入的启动信息已使用参数列表、关闭 Shell 执行并请求重定向输出，调用方可以把输出接入自己的日志。

`NeedsJava` 表示计划是否包含 Java 安装步骤，Java 的版本和可执行性由调用方检查。`VersionFiles` 记录本次已经成功写入版本目录的附加文件，供失败任务清理使用；不会把碰巧存在的同名文件记作本次产物。不要只检查文件是否存在，大小和 SHA-1 也应通过 `MissingFilesAsync` 校验。

## 各加载器怎么安装

| 加载器 | 处理方式 |
| --- | --- |
| Forge / NeoForge | 读取 Maven 安装器，解出内置支持库，下载依赖并执行客户端处理器。支持旧 `versionInfo` 格式和带 `processors` 的格式，跳过服务端步骤。 |
| Fabric / Quilt | 从官方 Meta API 获取启动配置，下载支持库，不执行图形安装向导。不会自动安装 Fabric API 等模组。 |
| LiteLoader | 获取构建元数据，生成 LaunchWrapper 配置并下载依赖。 |
| OptiFine | 获取官方文件地址，失败时使用镜像。需要补丁的发行包通过自带 `optifine.Patcher` 生成支持库，并提取发行包中的 LaunchWrapper。 |

安装完成后，`versions/<安装名>/` 中是一份完整 JSON 和同名客户端 JAR。最终 JSON 已合并原版支持库、Java 要求、资源索引、日志配置及启动参数，移除 `inheritsFrom` 和跨版本 `jar` 引用，不再依赖额外的原版版本目录。`clientVersion` 仍记录实际 Minecraft 版本，`libraries` 和 `assets` 在游戏目录下共享。安装名可以自定义，已有同名 JSON 会拒绝覆盖。

`baseVersionJsonPath` 和 `baseClientJarPath` 指定安装过程使用的原版元数据及 JAR 路径，Forge/NeoForge 处理器和 OptiFine 补丁也使用这些路径。省略时兼容旧调用方式，从原版版本目录读取；完成时会把 JAR 放进最终目录，但不会替调用方删除原有原版目录。调用方已经把 JAR 下载到最终位置时，不会再复制一份。

创建自己的安装任务前，可以调用 `MinecraftVanillaDownloadPlanner.ValidateVersionName(name)` 校验名称；目录跳转、路径分隔符、空名称和过长名称会被拒绝。

`ValidateSelections` 会检查组合。目前允许单个加载器，以及元数据明确兼容的 Forge + OptiFine；组合安装时先处理 Forge，再把生成的 OptiFine 文件放进这个版本的 `mods`。其他组合会拒绝。

## 来源、进度和失败处理

支持库和安装包按 `MinecraftManifestSource` 指定的官方／BMCLAPI 顺序尝试。并非所有元数据端点都有可用镜像，仍可能需要访问官方服务；不使用其他启动器的专用版本源。元数据异常保留各来源的失败信息，HTTP 超时和网络策略由传入的 `HttpClient` 决定。

LiteLoader 的旧官方链接包含指向 HTTP 的重定向。准备安装计划时，核心会尝试以 HTTPS 逐跳解析，最多处理 10 次跳转，取得实际构建文件地址，再交给下载队列。文件仍保存到共享 `libraries` 目录中的标准 Maven 路径。解析失败会通过 `log` 记录异常，保留原地址与镜像，让下载队列继续按来源优先级尝试；不会因此放开通用下载器的 HTTPS 降级限制。其未注明仓库的 `org.ow2.asm` 依赖使用 Maven Central，避免误向 Mojang 支持库站点请求不存在的文件。

下载进度由调用方的队列报告。`RunProcessorsAsync` 的百分比表示安装步骤完成比例，不是下载字节数。处理器退出后会检查声明的输出文件，有 SHA-1 时会校验哈希。

所有异步入口接受取消令牌。失败或取消后不应写入最终版本 JSON；调用方负责清理本次独占的工作目录和未完成目标版本，已验证的共享文件与原版可以保留。不要删除整个游戏目录。
