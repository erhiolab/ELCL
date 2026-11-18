# 整合包

命名空间：`ErhioLab.Craft.Launcher.Core.Modpacks`。

本页说明导入。扫描本地内容并生成整合包，请参阅[整合包导出](modpack-export.md)。

`ModpackArchive` 读取通用整合包，返回游戏版本、加载器、下载文件和覆盖文件。它不创建安装任务、不选择数据目录、不运行 Java，也不自行调用下载器。调用方决定安装顺序和进度展示。

支持 Modrinth、CurseForge 和 MultiMC 的格式版本 1，按包内清单识别。清单可在根目录或单层包装目录中；不支持普通备份 ZIP、自定义 MultiMC 组件或不明确的多个清单。

## ModpackArchive.ReadAsync

读取归档并解析声明，不发送网络请求，也不解压游戏文件。

```csharp
using ErhioLab.Craft.Launcher.Core.Modpacks;

ModpackDefinition pack = await ModpackArchive.ReadAsync("example.zip");
Console.WriteLine($"{pack.Name}：Minecraft {pack.GameVersion}");
foreach (var loader in pack.Loaders)
    Console.WriteLine($"{loader.Kind} {loader.Version.Version}");
```

`archivePath` 是本地 ZIP／MRPACK 路径；`cancellationToken` 可取消读取。返回 `ModpackDefinition`：

| 属性 | 含义 |
| --- | --- |
| `ArchivePath` | 归档绝对路径，提取覆盖文件时仍需要它。 |
| `Format` | `Modrinth`、`CurseForge` 或 `MultiMc`。 |
| `Name` | 包声明的名称；未声明时使用文件名。 |
| `GameVersion` | 明确声明的 Minecraft 版本，不从文件名猜测。 |
| `Loaders` | 可交给加载器安装器的 `MinecraftLoaderSelection` 列表。 |
| `Files` | 下载项，含相对路径、地址、大小和校验值，或 CurseForge 项目与文件 ID。 |
| `Overrides` | 归档内的附加文件、目标相对路径与大小，按应用顺序排列。 |

Modrinth 的 `client: unsupported` 文件会跳过，其他清单下载项保留。覆盖顺序为 `overrides/` 后 `client-overrides/`，同路径以客户端覆盖为准。MultiMC 导入游戏目录内容，不使用原实例的 Java 路径、启动命令或窗口配置。

归档会检查路径和数量，拒绝重复归档名称、符号链接、目录跳转、绝对路径及 Windows 保留名称。清单最多 8 MiB，归档最多 100000 项，单文件最多 8 GiB；声明的解压总量和下载总量分别最多 64 GiB。不会从覆盖目录导入常见启动器的私有配置与账户文件。

文件不存在或无法读取时抛出 I/O 异常；格式错误、不安全路径、重复下载目标或不兼容的加载器组合会失败，主要为 `InvalidDataException`、`JsonException` 或参数异常。不支持的组件抛出 `NotSupportedException`。取消时抛出 `OperationCanceledException`。

## ModpackArchive.ResolveDownloadsAsync

将声明转换为下载请求。Modrinth 已有地址时不需要访问网络；CurseForge 项目与文件 ID 需要查询平台。

```csharp
using ErhioLab.Craft.Launcher.Core.Modpacks;
using ErhioLab.Craft.Launcher.Core.Mods;

ModpackDefinition pack = await ModpackArchive.ReadAsync("example.zip");
string versionDirectory = Path.GetFullPath("game/versions/MyPack");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
IReadOnlyList<ModpackDownload> downloads = await ModpackArchive.ResolveDownloadsAsync(
    pack, versionDirectory, http,
    curseForgeApiKey: Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"),
    source: CommunitySource.OfficialFirst);
```

| 参数 | 用途 |
| --- | --- |
| `pack` | 通常使用 `ReadAsync` 返回的定义。 |
| `versionDirectory` | 调用方决定的隔离版本目录，所有下载目标限制在其中。 |
| `httpClient` | 调用方管理生命周期的客户端。 |
| `curseForgeApiKey` | 官方 CurseForge 查询需要；不会发送到 Modrinth 或镜像。 |
| `source` | `OfficialFirst` 或 `MirrorFirst`，默认官方优先。支持的社区 CDN 地址会添加备用来源。 |
| `cancellationToken` | 取消网络查询与规划。 |

返回的每个 `ModpackDownload` 包含 `Request` 和可空的 `Sha512`。`Request` 是现有下载器接受的 `DownloadFileRequest`，包含目标路径、候选地址、可空大小与 SHA-1。

方法不下载文件、不创建目标目录。它会校验目标路径，拒绝替换版本同名 JSON／JAR 或启动器配置，也会检查 CurseForge 文件归属与第三方下载许可。不可分发或没有下载地址时抛出 `InvalidOperationException`；路径或文件归属异常主要为 `InvalidDataException`。平台失败保留网络或解析异常，取消保留 `OperationCanceledException`。

## ModpackArchive.VerifyDownloadsAsync

检查文件是否存在、大小是否匹配，并验证提供的 SHA-1 与 SHA-512。方法只读取文件，不修改或删除它们。

下面的完整示例说明规划、下载和验证之间的关系；实际调用前应先安装对应游戏与加载器：

```csharp
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Modpacks;

ModpackDefinition pack = await ModpackArchive.ReadAsync("example.zip");
string versionDirectory = Path.GetFullPath("game/versions/MyPack");
Directory.CreateDirectory(versionDirectory);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var downloads = await ModpackArchive.ResolveDownloadsAsync(pack, versionDirectory, http,
    Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"));
await using var manager = new DownloadManager(http);
var handle = manager.Enqueue(new DownloadTaskRequest(pack.Name,
    downloads.Select(file => file.Request).ToArray()), new DownloadOptions(8));
await handle.Completion;
await ModpackArchive.VerifyDownloadsAsync(downloads);
```

`downloads` 是待验证的计划，`cancellationToken` 可取消读取。大小或摘要不匹配时抛出 `InvalidDataException`，不可读文件保留 I/O 异常。验证失败时，调用方决定重试、显示错误或清理本次安装。

## ModpackArchive.ExtractOverridesAsync

将配置、存档等附加文件解压到指定版本目录。应在清单文件下载并验证后调用，让覆盖文件按包作者的声明生效。

```csharp
using ErhioLab.Craft.Launcher.Core.Modpacks;

ModpackDefinition pack = await ModpackArchive.ReadAsync("example.zip");
string versionDirectory = Path.GetFullPath("game/versions/MyPack");
var progress = new Progress<double>(percent => Console.WriteLine($"{percent:0.0}%"));
await ModpackArchive.ExtractOverridesAsync(pack, versionDirectory, progress);
```

`pack` 的源归档必须仍可读取，`versionDirectory` 应是本次安装拥有的新目录；`progress` 可选，以解压字节计算百分比，范围为 0～100；`cancellationToken` 可取消复制。

方法创建必要的子目录，文件先写入临时文件，再替换目标。覆盖目录内已有同路径文件会被替换，整个方法不提供事务回滚；失败后已完成的文件保留，当前临时文件清理，调用方负责本次版本的回滚。目标路径越界、链接目录、版本元数据覆盖或归档条目发生变化时抛出 `InvalidDataException`，其他 I/O 与取消异常保持原义。

## 组合安装流程

建议先读取归档、让调用方确认新版本名称与目标目录，再获取对应的游戏元数据，使用[原版安装规划](vanilla-installation.md)和[加载器安装](loader-installation.md)完成基础安装。然后解析下载项、交给下载器、验证摘要，最后提取覆盖文件。

游戏的 `libraries/`、`assets/` 仍使用游戏根目录的共享位置。整合包的文件目标和覆盖目标使用新版本目录。不要把原启动器实例配置当成可执行安装指令，也不要自动执行归档内脚本。
