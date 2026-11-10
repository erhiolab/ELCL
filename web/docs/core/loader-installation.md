# 安装模组加载器

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

支持 Forge、NeoForge、Fabric、Quilt、LiteLoader 与 OptiFine。安装分为准备计划、下载依赖、执行处理器、写入版本四步，不能只写完 JSON 就宣布成功。

## 示例准备

先按[原版安装计划](vanilla-installation.md)准备原版 JSON 和客户端 JAR。推荐把 JSON 放进工作目录，JAR 直接下载到最终安装名目录，避免额外留一个原版实例。

以下示例共用这段初始化；`work/1.21.1.json` 必须保留原版 `id`，客户端 JAR 也必须已完整下载。

```csharp
using System.Diagnostics;
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
await using var downloads = new DownloadManager(http);
var installer = new MinecraftLoaderInstaller(http);
CancellationToken cancellationToken = default;
string gameDirectory = Path.GetFullPath(".minecraft");
string workDirectory = Path.GetFullPath("work/loader-install");
string originalJsonPath = Path.GetFullPath("work/1.21.1.json");

var catalog = await new MinecraftLoaderVersionClient(http).GetVersionsAsync(
    MinecraftLoaderKind.NeoForge, "1.21.1", MinecraftManifestSource.Official,
    cancellationToken);
var selections = new[]
{
    new MinecraftLoaderSelection(MinecraftLoaderKind.NeoForge, catalog.Versions.First())
};
```

## `MinecraftLoaderInstaller.DefaultVersionName` {#minecraftloaderinstaller-defaultversionname}

根据游戏版本和加载器选择生成默认安装名。

```csharp
string name = MinecraftLoaderInstaller.DefaultVersionName("1.21.1", selections);
string clientJarPath = Path.Combine(gameDirectory, "versions", name, name + ".jar");
```

例如选择 NeoForge `21.1.81` 时得到 `1.21.1-NeoForge_21.1.81`。返回字符串，不创建文件；用户自定义名称时仍需自行校验冲突。

## `MinecraftLoaderInstaller.ValidateSelections` {#minecraftloaderinstaller-validateselections}

```csharp
MinecraftLoaderInstaller.ValidateSelections("1.21.1", selections);
MinecraftVanillaDownloadPlanner.ValidateVersionName(name);
```

校验游戏和加载器版本标识，以及两两组合兼容性；不兼容时抛出 `ArgumentException`。没有选择加载器的情况由后续 `PrepareAsync` 拒绝。

当前允许单个加载器，以及明确兼容的 Forge＋OptiFine。不会安装 Fabric API 或其他普通模组。

## `MinecraftLoaderInstaller.PrepareAsync` {#minecraftloaderinstaller-prepareasync}

读取加载器元数据与安装包，生成 `MinecraftLoaderInstallPlan`。这一步可能下载文件、解压安装器和写入工作文件，不是纯解析。

```csharp
async Task DownloadFiles(IReadOnlyList<DownloadFileRequest> files, CancellationToken token)
{
    var unique = files.DistinctBy(file => file.DestinationPath).ToArray();
    var missing = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(unique, token);
    if (missing.Count > 0)
        await downloads.Enqueue(new DownloadTaskRequest(name, missing),
            new DownloadOptions(8), cancellationToken: token).Completion;
}

var plan = await installer.PrepareAsync("1.21.1", name, selections,
    gameDirectory, workDirectory, MinecraftManifestSource.Official,
    DownloadFiles, log: Console.WriteLine, cancellationToken: cancellationToken,
    baseVersionJsonPath: originalJsonPath, baseClientJarPath: clientJarPath);
await DownloadFiles(plan.Files, cancellationToken);
```

| 参数 | 含义 |
| --- | --- |
| `gameVersion`、`installedName`、`selections` | 实际原版 ID、最终目录名与加载器选择。 |
| `gameDirectory`、`workDirectory` | 游戏根目录和本次安装独占工作目录。 |
| `preferredSource` | 支持的官方／镜像地址优先顺序。 |
| `download` | 下载并校验这一批文件的异步回调。 |
| `log`、`cancellationToken` | 可选的文本日志回调和取消令牌。 |
| `baseVersionJsonPath`、`baseClientJarPath` | 已准备的原版 JSON 与 JAR。省略时从原版版本目录读取。 |

`plan.Files` 是准备阶段后仍需补齐的依赖，调用方必须等待下载成功。`NeedsJava` 表示是否存在 Java 安装步骤；`VersionFiles` 记录本次已写入版本目录的附加文件，可用于清理。

## `MinecraftLoaderInstaller.RunProcessorsAsync` {#minecraftloaderinstaller-runprocessorsasync}

依赖下载完成后执行客户端处理器或补丁工具。

```csharp
string? javaPath = plan.NeedsJava
    ? (await JavaDiscovery.FindAsync(cancellationToken: cancellationToken)).First().Path
    : null;
await installer.RunProcessorsAsync(plan, javaPath, RunJavaAsync,
    progress: new Progress<double>(value => Console.WriteLine($"安装步骤：{value:0}%")),
    cancellationToken: cancellationToken);
```

`execute` 是调用方提供的执行回调，下节给出实现。应按安装器要求选择 Java，示例的第一个运行时只是简化用法。

进度是处理器步骤完成比例，范围 0～100，不是下载字节数。已验证输出可跳过，执行后会检查声明的产物及可用 SHA-1。

## `MinecraftLoaderInstaller.WriteVersionAsync` {#minecraftloaderinstaller-writeversionasync}

只在前面全部成功后调用。

```csharp
await MinecraftLoaderInstaller.WriteVersionAsync(plan, cancellationToken);
```

合并原版和加载器信息，写入完整 JSON 和同名客户端 JAR，移除跨版本的 `inheritsFrom`、`jar` 引用，保留 `clientVersion`。

同名 JSON 已存在时拒绝覆盖。最终目录只有一个可选版本；支持库与资源仍共享。省略原版路径而使用旧原版目录的调用，不会自动删除那个目录。

## 执行 Java 的回调

传入的启动信息已使用参数列表并重定向输出。下面的回调等待退出、同时读取两个输出流，并在取消时结束进程树。

```csharp
static async Task RunJavaAsync(ProcessStartInfo start, CancellationToken token)
{
    using var process = Process.Start(start)
        ?? throw new IOException("无法创建安装进程。");
    Task<string> output = process.StandardOutput.ReadToEndAsync();
    Task<string> error = process.StandardError.ReadToEndAsync();
    try
    {
        await process.WaitForExitAsync(token);
    }
    catch (OperationCanceledException)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        throw;
    }
    Console.WriteLine(await output);
    Console.Error.WriteLine(await error);
    if (process.ExitCode != 0)
        throw new IOException($"安装进程退出码：{process.ExitCode}");
}
```

生产应用可将输出接入自己的日志。仅取消 `WaitForExitAsync` 不会让进程停下，不能因此开始删除它正在写入的目录。

## 各加载器的差别

| 加载器 | 处理内容 |
| --- | --- |
| Forge／NeoForge | 读取安装器、提取内置库、准备依赖、执行客户端处理器，跳过服务端步骤。 |
| Fabric／Quilt | 取得 Meta 启动配置并下载依赖，不运行图形安装器。 |
| LiteLoader | 构建 LaunchWrapper 配置，兼容旧构建地址和依赖。 |
| OptiFine | 获取发行包，必要时调用补丁工具生成支持库；与 Forge 组合时写入版本的模组目录。 |

LiteLoader 旧地址的 HTTPS 逐跳解析由安装器处理，最多 10 次。失败会记入 `log` 并保留候选来源，不会放宽通用下载器的 HTTPS 降级限制。

失败或取消后不要写入最终 JSON。清理本次独占工作目录和未完成版本，保留已经验证的共享文件；`VersionFiles` 不能替代应用对所有本次下载产物的跟踪。
