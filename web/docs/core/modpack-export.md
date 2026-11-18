# 整合包导出

命名空间：`ErhioLab.Craft.Launcher.Core.Modpacks`。

导出分为扫描与写入两步。核心返回分类和默认勾选建议，具体选择、保存位置与任务展示由调用方决定。输出是带 `modrinth.index.json` 的 ZIP，可使用 `.mrpack` 扩展名。

## ModpackExporter.ScanAsync

读取本地版本的继承元数据、Minecraft 与加载器依赖，扫描版本目录中的文件，不发送网络请求，也不修改源目录。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Modpacks;

string gameDirectory = Path.GetFullPath("game");
LocalMinecraftVersion version = LocalMinecraftVersionScanner.Scan(gameDirectory)
    .First(item => item.IsValid);
ModpackExportScan scan = await ModpackExporter.ScanAsync(gameDirectory, version);
foreach (ModpackExportFile file in scan.Files)
    Console.WriteLine($"{file.Path}：{file.Category}，{file.Size} 字节");
```

| 参数 | 用途 |
| --- | --- |
| `gameDirectory` | Minecraft 根目录，用于读取继承的父版本元数据。 |
| `version` | 要扫描的本地版本，包含版本目录与 JSON 路径。 |
| `cancellationToken` | 取消元数据读取和目录遍历。 |

返回的 `ModpackExportScan` 包含 `VersionDirectory`、`GameVersion`、必选 `Dependencies` 和 `Files`。每个文件记录相对 `Path`、`Size`、`LastWriteTimeUtc`、`Category`、`DefaultSelected` 与 `CanSelect`。

游戏设置、启用的 Mods、配置、材质包和光影建议默认选择；存档、个人记录、日志、截图、缓存、禁用 Mods 与未知文件默认不选。运行文件与启动器数据不可选择。链接本身会列为不可选择的条目，不遍历其目标。

父版本缺失保留 `FileNotFoundException`；元数据无效、链接根目录或文件条目超过 99000 个时抛出 `InvalidDataException`，其他读取错误保留 I/O 或访问异常。取消抛出 `OperationCanceledException`。扫描没有获取缺失父版本的网络回调。

## ModpackExporter.WriteAsync

按扫描快照与明确的文件路径写入整合包。游戏本体由依赖清单表示，不复制版本 JSON、客户端、支持库、资源库或启动器配置。

下面是可独立使用的完整示例，直接打包默认选中的资源，不需要网络客户端：

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Core.Modpacks;

string gameDirectory = Path.GetFullPath("game");
var version = LocalMinecraftVersionScanner.Scan(gameDirectory).First(item => item.IsValid);
var scan = await ModpackExporter.ScanAsync(gameDirectory, version);
var options = new ModpackExportOptions(
    "我的整合包", "1.0.0",
    scan.Files.Where(file => file.DefaultSelected).Select(file => file.Path).ToArray(),
    BundleResources: true);
var progress = new Progress<ModpackExportProgress>(value =>
    Console.WriteLine($"{value.Percent:0.0}% {value.Path}"));
ModpackExportResult result = await ModpackExporter.WriteAsync(
    scan, options, Path.GetFullPath("MyPack.mrpack"), progress: progress);
Console.WriteLine($"打包 {result.EmbeddedFiles} 个文件");
```

| 参数 | 用途 |
| --- | --- |
| `scan` | `ScanAsync` 返回的目录、依赖与文件快照。 |
| `options` | 名称、版本、明确选择的相对文件路径及导出选项。 |
| `outputPath` | 版本目录之外的目标路径。 |
| `repository` | 未直接打包资源时必填，使用调用方初始化的 `ModRepositoryClient`。 |
| `progress` | 可选进度回调，含 0～100 的百分比及当前文件。来源查询时百分比保持为零，写入时按文件字节推进。 |
| `onLookupFailure` | 可选的来源查询失败回调，参数是相对文件路径与异常；应避免在回调中抛出异常。 |
| `cancellationToken` | 取消来源查询、文件读取和归档写入。 |

| `ModpackExportOptions` 属性 | 行为 |
| --- | --- |
| `Name`／`Version` | 包名最多 128 字符，版本最多 64 字符，不能为空或含控制字符。 |
| `SelectedPaths` | 具体相对文件路径列表，按扫描结果核对并去重，不解释通配符。 |
| `BundleResources` | 默认 `false`。为 `true` 时，选中的资源全部内嵌，不查询来源。 |
| `OnlyModrinth` | 默认 `false`。来源匹配是否仅使用 Modrinth。 |
| `OverwriteExisting` | 默认 `false`。调用方明确允许覆盖后才应设为 `true`。 |

查询模式通过 [`ModRepositoryClient.FindFileAsync`](mods.md#modrepositoryclient-findfileasync) 查找选中的 Mods、材质包与光影。最多同时查询四个文件，只接受大小与 SHA-1 完全相同的 HTTPS 下载项，并写入本地计算的 SHA-1 与 SHA-512。未匹配、来源拒绝下载或查询失败的文件改为内嵌，普通配置始终内嵌。取消不会降级成内嵌。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

// 在上一段完成 scan 与 options 初始化后，将资源改为来源查询模式。
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
var repository = new ModRepositoryClient(http,
    Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"));
var linkedOptions = options with { BundleResources = false, OnlyModrinth = true };
await ModpackExporter.WriteAsync(scan, linkedOptions,
    Path.GetFullPath("LinkedPack.mrpack"), repository);
```

返回 `ModpackExportResult`：`EmbeddedFiles` 是内嵌数，`ReferencedFiles` 是下载引用数，`LookupFailures` 是查询失败数，`SelectedBytes` 是所选源文件总字节数。

所选文件与扫描快照的长度、修改时间不符时抛出 `IOException`，应重新扫描后让用户确认选择。路径越界、链接、不可选文件、缺少游戏依赖或超过限制时抛出 `InvalidDataException`；参数错误保留参数异常。来源查询失败通过回调报告并内嵌，源文件读取与写入失败直接抛出。

归档最多 99000 个源文件，单个文件最多 8 GiB，总量最多 64 GiB，清单最多 8 MiB。数据写入目标旁的独立临时文件，完成后才替换目标；失败或取消清理临时文件，保留已有目标。方法创建目标父目录，但不删除源目录，不执行文件，不附带任何启动器程序。

Forge、NeoForge、Fabric 与 Quilt 使用常见依赖名；LiteLoader 与 OptiFine 为扩展名称，ELCLC 导入器支持，其他工具不一定支持。通用清单不保存任意自定义启动组件或被修改的客户端。
