# 安装原版 Minecraft

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

`MinecraftVanillaDownloadPlanner` 从版本 JSON 与资源索引生成文件请求。它不调度任务；本页示例使用[下载器](downloader.md)执行请求。

## 示例准备

下面各节按顺序拼在一个控制台程序中，就是一次原版安装。请使用一个尚未安装的名称，运行时会下载实际游戏文件。

```csharp
using ErhioLab.Craft.Launcher.Core.Downloads;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
await using var downloads = new DownloadManager(http);
CancellationToken cancellationToken = default;
var source = MinecraftManifestSource.Official;
string gameDirectory = Path.GetFullPath(".minecraft");
string installedName = "1.21.1";

var catalog = await new MinecraftVersionCatalogClient(http)
    .GetCatalogAsync(source, cancellationToken);
var selected = catalog.Versions.First(version => version.Id == "1.21.1");
var options = new DownloadOptions(8);
```

## `MinecraftVanillaDownloadPlanner.ValidateVersionName` {#minecraftvanilladownloadplanner-validateversionname}

在创建目标路径前校验安装名称。

```csharp
MinecraftVanillaDownloadPlanner.ValidateVersionName(installedName);
```

拒绝空白、过长、路径分隔符、目录跳转以及当前平台不允许的文件名字符。不符合要求时抛出 `ArgumentException`。

此方法不检查磁盘上是否已存在同名版本。覆盖策略仍由应用决定。

## `MinecraftVanillaDownloadPlanner.VersionMetadata` {#minecraftvanilladownloadplanner-versionmetadata}

从清单条目生成版本 JSON 的下载请求。

```csharp
string metadataPath = Path.GetFullPath("work/1.21.1.json");
var metadata = MinecraftVanillaDownloadPlanner.VersionMetadata(
    selected, metadataPath, source);
await downloads.Enqueue(new DownloadTaskRequest("版本元数据", new[] { metadata }),
    options, cancellationToken: cancellationToken).Completion;
```

| 参数 | 含义 |
| --- | --- |
| `version` | 从游戏清单中选中的 `MinecraftVersion`。 |
| `destination` | 缓存版本 JSON 的路径，不必放入正式版本目录。 |
| `preferredSource` | 官方／镜像优先顺序。 |

返回的 `DownloadFileRequest` 带来源和可用的 SHA-1，调用本方法本身不下载。

## `MinecraftVanillaDownloadPlanner.ParseVersion` {#minecraftvanilladownloadplanner-parseversion}

解析原版 JSON，生成客户端、支持库和资源索引等请求。

```csharp
string json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
var plan = MinecraftVanillaDownloadPlanner.ParseVersion(
    json, selected.Id, gameDirectory, installedName, source);

await downloads.Enqueue(new DownloadTaskRequest("资源索引", new[] { plan.AssetIndex }),
    options, cancellationToken: cancellationToken).Completion;
```

`expectedVersionId` 应传原版 ID，`installedName` 可以是自定义安装名。JSON 的 `id` 不匹配时抛出 `InvalidDataException`，防止安装错误版本。

| 计划字段 | 含义 |
| --- | --- |
| `Client` | 保存到 `versions/<安装名>/<安装名>.jar`。 |
| `Libraries` | 按当前操作系统筛选的共享支持库。 |
| `AssetIndex`、`AssetIndexId` | 共享资源索引请求与 ID。 |
| `LoggingConfiguration` | 可为空的客户端日志配置请求。 |
| `AssetTotalSize` | 元数据提供的资源总大小，可能为空。 |
| `VersionJson` | 后续写入安装结果需要的原始 JSON。 |

方法只生成计划，不创建完整安装；支持库位于游戏根目录的 `libraries`，不会每个版本复制一份。

## `MinecraftVanillaDownloadPlanner.ParseAssets` {#minecraftvanilladownloadplanner-parseassets}

资源索引下载完成后，生成全部资源文件请求。

```csharp
string indexJson = await File.ReadAllTextAsync(plan.AssetIndex.DestinationPath, cancellationToken);
var assets = MinecraftVanillaDownloadPlanner.ParseAssets(
    indexJson, gameDirectory, source);
```

根据每个对象的 SHA-1 生成 `assets/objects/<前两位>/<完整哈希>` 路径，同一对象只返回一次。索引缺少必要字段或哈希无效时抛出数据／JSON 异常。

这里不生成旧版的 `resources` 映射，启动前可用[旧版资源映射方法](launch.md)处理。

## `MinecraftVanillaDownloadPlanner.MissingFilesAsync` {#minecraftvanilladownloadplanner-missingfilesasync}

检查文件是否存在，再按提供的大小和 SHA-1 校验，返回需要下载的条目。

```csharp
var files = plan.Libraries.Append(plan.Client).Append(plan.AssetIndex)
    .Concat(assets).ToList();
if (plan.LoggingConfiguration is { } logging) files.Add(logging);

var missing = await MinecraftVanillaDownloadPlanner.MissingFilesAsync(
    files, cancellationToken);
if (missing.Count > 0)
    await downloads.Enqueue(new DownloadTaskRequest("游戏文件", missing),
        options, cancellationToken: cancellationToken).Completion;
```

没有大小或哈希时只能使用已提供的条件判断；二者都没有时，存在即视为可用。它不删除损坏文件、不下载，也不自动合并重复目标路径。

读取已有文件遇到权限或 I/O 问题会抛出异常，不会简单当作“文件不存在”。

## `MinecraftVanillaDownloadPlanner.WriteInstalledVersionAsync` {#minecraftvanilladownloadplanner-writeinstalledversionasync}

全部文件准备成功后，最后写入正式版本 JSON。

```csharp
await MinecraftVanillaDownloadPlanner.WriteInstalledVersionAsync(
    plan, gameDirectory, installedName, cancellationToken);
```

使用临时文件替换，JSON 的 `id` 改为安装名。此方法会替换同名 JSON，不再替你校验所有文件；应用应提前检查同名冲突，只在下载成功后调用。

失败或取消时，由调用方清理本次未完成目标和独占工作目录。已经校验的共享支持库与资源可以保留。

## 安装加载器时怎样复用

将原版 JSON 留在工作目录，把客户端 JAR 下载到最终加载器安装目录，再将这两个路径传给[加载器安装器](loader-installation.md)即可。不必先创建另一个可选的原版版本。
