# 社区 Mods

命名空间：`ErhioLab.Craft.Launcher.Core.Mods`。

`ModRepositoryClient` 查询 CurseForge 与 Modrinth，返回项目、文件和必需依赖计划。`LocalModManager` 读取本地 Mods 并处理文件操作；`McModTranslationClient` 查询 MC 百科译名。

核心不保存 API Key，不选择安装目录，也不创建启动器任务。你提供 `HttpClient`、密钥与路径，再决定怎样展示和下载。

## ModRepositoryClient 构造方法

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
string? key = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
var client = new ModRepositoryClient(http, key, CommunitySource.OfficialFirst);
```

`httpClient` 必填，由调用方管理生命周期。官方 CurseForge API 需要调用方提供 `curseForgeApiKey`；只查询 Modrinth 时可以省略。Key 只附加到官方 CurseForge 请求，不发送给 Modrinth 或镜像。

`source` 为 `OfficialFirst` 或 `MirrorFirst`，默认官方优先。另一来源作为失败回退，镜像使用 MCIM；两者都无法使用时抛出异常。不为自定义地址猜测镜像。

## ModRepositoryClient.SearchAsync

按名称、游戏版本、加载器和类型搜索，可选择相关性或下载量排序。返回 `ModSearchResult`；这个方法只请求元数据，不下载 Mods 文件。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new ModRepositoryClient(http,
    Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"));
var result = await client.SearchAsync(new ModSearchRequest(
    Query: "sodium",
    Platform: ModPlatform.All,
    GameVersion: "1.21.1",
    Loader: "fabric",
    Page: 0,
    PageSize: 20,
    SortOrder: ModSortOrder.Relevance));
foreach (var project in result.Projects)
    Console.WriteLine($"{project.Platform}: {project.Name}，{project.Downloads} 次下载");
```

| 参数 | 用法 |
| --- | --- |
| `Query` | 名称关键词，空字符串表示不限制。此接口不自动翻译中文关键词。 |
| `Platform` | `All`、`CurseForge` 或 `Modrinth`。 |
| `GameVersion` | 完整游戏版本，例如 `1.21.1`；空值表示不限。 |
| `Loader` | `forge`、`neoforge`、`fabric`、`quilt` 或 `liteloader`，空值表示不限。 |
| `Category` | 可选 `ModCategory`。由调用方给出 Modrinth 分类标签与 CurseForge 分类 ID。 |
| `Page` | 从 0 开始，支持 0～499。 |
| `PageSize` | 每页 1～50 项，默认 20。 |
| `SortOrder` | `ModSortOrder.Relevance` 为相关性排序，也是默认值；`ModSortOrder.Downloads` 为下载量降序。 |

相关性排序会请求 Modrinth 的 `relevance` 和 CurseForge 的相关性排序。选择单一平台时保留上游顺序；选择“全部”时，按各平台的结果名次交替合并，先显示两个来源的第一项，再显示第二项，以此类推。同名次固定先显示 Modrinth；不会使用下载量覆盖上游相关性顺序，也不比较两平台各自的相关性分数。

下载量排序会请求 Modrinth 的 `downloads` 和 CurseForge 的总下载量排序，再按下载次数降序合并。每个平台的下载量独立统计，合并顺序只比较所返回的数值。

两种方式都会取得足够的排序前缀，再合并分页，按平台与项目 ID 去重；同一作品在不同平台的发布仍是不同项目。请求后续页时应保留关键词、筛选条件和 `SortOrder`，更换排序时从 `Page: 0` 开始。翻到较后页面需要更多上游请求，建议调用方缓存搜索结果。

`Total` 是成功来源报告的结果数之和；它不是跨平台去重后的作品数。是否继续翻页以 `HasMore` 为准。CurseForge 最多访问前 10000 个结果。

单个平台失败时返回其他平台结果，在 `FailedPlatforms` 中标明失败来源，并通过 `SourceErrors` 保留对应异常，方便调用方记录日志；全部失败抛出 `AggregateException`。页码、每页数量越界或 `SortOrder` 不属于支持的枚举值时抛出 `ArgumentOutOfRangeException`。请求、解析与取消异常见[错误处理](#错误处理)。

分类例子：`new ModCategory("optimization", "optimization", 6814)`。任一平台的分类缺失时，该平台不返回这个分类的结果；不要把不同语义的分类 ID 混用。

## ModRepositoryClient.GetProjectAsync

根据平台与 ID 或平台支持的别名读取单个项目。Modrinth 支持 slug；CurseForge 使用数字 ID。不能传入 `All`。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient();
var client = new ModRepositoryClient(http);
ModProject project = await client.GetProjectAsync(ModPlatform.Modrinth, "sodium");
Console.WriteLine(project.Website);
```

返回 `ModProject`，包含名称、简介、图标地址、下载量、更新时间、已知游戏版本和加载器。CurseForge 的已知版本来自上游最新文件索引，不代表全部历史版本。

项目不存在时抛出状态为 404 的 `HttpRequestException`；不会创建本地文件。

## ModRepositoryClient.GetFileAsync

按项目 ID 与文件 ID 读取一个确切文件，适合处理 CurseForge 整合包中固定的文件引用。不会下载文件内容。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient();
var client = new ModRepositoryClient(http,
    Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"));
ModFile file = await client.GetFileAsync(ModPlatform.CurseForge, "238222", "5877199");
Console.WriteLine(file.FileName);
```

`platform` 必须是 `CurseForge` 或 `Modrinth`，不能使用 `All`；`projectId`、`fileId` 均必填，使用平台提供的准确 ID，`cancellationToken` 可取消请求。返回 `ModFile`，包含文件名、下载地址、校验信息、适用版本与声明的前置。

此方法检查返回文件的项目与文件 ID 是否匹配，不匹配时抛出 `InvalidDataException`；空 ID 或不支持的平台值会抛出参数异常。CurseForge 官方查询需要调用方提供 API Key。网络、解析与取消异常保持原义。没有下载地址时，调用方应保留不可下载状态，不能假设能从其他地址取得文件。

## ModRepositoryClient.GetFilesAsync

读取项目的可下载版本。可以按游戏版本与加载器筛选，返回 `ModFilePage`。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient();
var client = new ModRepositoryClient(http);
var project = await client.GetProjectAsync(ModPlatform.Modrinth, "sodium");
var page = await client.GetFilesAsync(project, "1.21.1", "fabric", page: 0, pageSize: 50);
foreach (var file in page.Files)
    Console.WriteLine($"{file.Name}，{file.Published}，{file.FileName}");
```

`project` 是上一步获得的项目。`gameVersion`、`loader` 省略时不限；`page` 从 0 开始，范围 0～499，`pageSize` 为 1～50。`Files` 按发布时间从新到旧排列，继续获取下一页前检查 `HasMore`。

Modrinth 接口一次返回筛选后的版本，请求省略更新日志正文以减少传输，核心再分页；CurseForge 使用上游分页，最多前 10000 项。没有匹配文件时返回空列表，不代表请求失败。

`DownloadUri` 可能为空，尤其是作者限制第三方下载的项目。此时应提供来源页面入口，不应自行拼出 CDN 地址绕过限制。返回信息还包括 SHA-1、可用的 SHA-512 和必需依赖。

Modrinth 的每个版本选取主文件优先的 JAR；缺少 JAR 的异常条目会抛出 `InvalidDataException`。方法不下载文件。

## ModRepositoryClient.GetAllFilesAsync

获取匹配条件下的完整文件列表，适合由应用一次加载并在本地分组、筛选。参数 `project`、`gameVersion`、`loader` 与 `GetFilesAsync` 一致；可选 `progress` 报告已读取的文件数量，取消令牌可以中止剩余请求。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new ModRepositoryClient(http,
    Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"));
var project = await client.GetProjectAsync(ModPlatform.Modrinth, "sodium");
var progress = new Progress<int>(count => Console.WriteLine($"已读取 {count} 个文件"));
IReadOnlyList<ModFile> files = await client.GetAllFilesAsync(
    project, gameVersion: "1.21.1", progress: progress);
foreach (var file in files) Console.WriteLine(file.Name);
```

返回值按发布时间从新到旧排列。Modrinth 一次取得匹配的版本，省略更新日志正文，不反复请求整份列表；CurseForge 优先在一次请求中获取最多 10000 个文件的元数据；来源只返回部分结果或不支持大页时，再自动翻页补齐，每批最多四个并行请求，并去除重复文件 ID。受上游限制，最多取得前 10000 个文件。没有匹配项时返回空列表。

方法不下载 Mods、不修改磁盘。主动取消抛出 `OperationCanceledException`；任一请求失败会抛出对应异常，调用方不能把这次读取当成完整结果。参数为空或平台无效时抛出参数异常，其余网络及解析异常与 `GetFilesAsync` 一致。

## ModRepositoryClient.IdentifyAsync

通过本地文件的 SHA-1 或 CurseForge 指纹查找来源项目，不上传文件内容。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient();
var client = new ModRepositoryClient(http,
    Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"));
var local = await LocalModManager.ReadAsync(@"D:\Minecraft\mods\example.jar");
ModProject? project = await client.IdentifyAsync(local);
Console.WriteLine(project?.Website.ToString() ?? "没有找到来源");
```

`local` 应来自 `ReadAsync` 或成功解析的扫描项。先查询 Modrinth；明确返回未找到后，再使用 CurseForge 指纹查询。未提供 CurseForge Key 时不查询其指纹接口。

两个平台都没有匹配时返回 `null`；网络故障会抛异常，应与“未找到”分别处理。本方法不改变本地文件。

## ModRepositoryClient.GetDependenciesAsync

查询给定依赖记录对应的 Mods 项目，返回去重后按下载量降序排列的 `IReadOnlyList<ModProject>`。适合展示前置 Mods 名称、图标与详情入口。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient();
var client = new ModRepositoryClient(http);
var project = await client.GetProjectAsync(ModPlatform.Modrinth, "sodium");
var files = await client.GetFilesAsync(project, "1.21.1", "fabric");
if (files.Files.FirstOrDefault() is { } file)
{
    var dependencies = await client.GetDependenciesAsync(project.Platform, file.RequiredDependencies);
    foreach (var dependency in dependencies)
        Console.WriteLine($"{dependency.Name}：{dependency.Website}");
}
```

`platform` 必须是 `CurseForge` 或 `Modrinth`，不能传入 `All`。`dependencies` 为同平台的依赖记录，例如 `ModFile.RequiredDependencies`；有项目 ID 时直接查询，Modrinth 只有版本 ID 时先读取该版本所属项目。`cancellationToken` 可以取消请求。

相同项目 ID 只请求一次，最多同时查询四项，接受最多 128 项不同依赖。空集合返回空列表，空参数抛出 `ArgumentNullException`，超过数量限制或缺少标识抛出 `InvalidDataException`。任一查询失败时抛出异常，不返回不完整列表；请求、解析与取消异常见[错误处理](#错误处理)。

此方法不递归读取前置的前置，不选择下载文件、不执行下载，也不写入磁盘。返回的项目列表不能代替某个文件的精确依赖约束；需要生成完整依赖下载计划时，另行调用下一方法。

## ModRepositoryClient.CreateDownloadPlanAsync

为一个明确选择的文件递归解析必需依赖，返回按依赖在前、主 Mods 在后排列的 `IReadOnlyList<ModDownloadItem>`。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient();
var client = new ModRepositoryClient(http);
var project = await client.GetProjectAsync(ModPlatform.Modrinth, "sodium");
var files = await client.GetFilesAsync(project, "1.21.1", "fabric");
if (files.Files.Count == 0) return;
var plan = await client.CreateDownloadPlanAsync(project, files.Files[0], "1.21.1", "fabric");
foreach (var item in plan)
    Console.WriteLine($"{item.Project.Name}：{item.File.DownloadUri}");
```

`project` 与 `file` 必须属于同一平台与项目。`gameVersion`、`loader` 用于选择兼容依赖，安装到具体游戏时应传入这两项。Modrinth 明确指定的依赖版本会被保留；否则优先选兼容的最新正式版，没有正式版时选最新测试版本。

可选依赖不会加入计划，也不会检查你的磁盘。循环依赖不会无限递归；同一项目要求冲突版本、缺少兼容依赖或超过 128 个项目时抛出 `InvalidDataException`。文件不属于项目时抛出 `ArgumentException`；禁止第三方下载或没有文件地址时抛出 `InvalidOperationException`。

这个方法只查询元数据，不下载、不安装、不替换已有 Mods。调用方负责识别已安装的相同文件、处理版本冲突，再将计划交给[下载器](downloader.md)。

## ModRepositoryClient.DownloadSources

为支持镜像的文件 URL 生成有序来源列表，适用于 `DownloadFileRequest.Sources`。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

var official = new Uri("https://cdn.modrinth.com/data/example/versions/example/mod.jar");
IReadOnlyList<Uri> sources = ModRepositoryClient.DownloadSources(official, CommunitySource.MirrorFirst);
foreach (var source in sources) Console.WriteLine(source);
```

根据 `source` 返回官方与 MCIM 的先后顺序。仅转换明确支持的 Modrinth CDN 与 Forge CDN 主机；其他 URL 原样返回。不会进行网络请求，也不会访问磁盘。

## McModTranslationClient 构造方法

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var translations = new McModTranslationClient(http);
```

传入可复用的 `HttpClient`，生命周期仍由调用方管理。核心包含压缩的 MC 百科词条映射快照，不保存用户的查询缓存。原始来源与署名保留在 `Mods/Data/NOTICE.md`。

## McModTranslationClient.FindAsync

先从内置 MC 百科词条索引查找中文名、英文名、词条地址和来源 slug，未命中时再查询百科公开页面。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var translations = new McModTranslationClient(http);
var entry = await translations.FindAsync("Sodium", modrinthSlug: "sodium");
Console.WriteLine(entry?.ChineseName ?? "Sodium");
```

`name` 必填；已知来源时传入 `curseForgeSlug` 或 `modrinthSlug`，用于核实词条。仅查询中文名称时，也可以只传 `name`，从结果读取英文名作为平台搜索关键词。

有平台 slug 时优先精确匹配平台身份；没有平台身份时才按明确的中文名、英文名或 slug 查找。索引命中不会发送 HTTP 请求。少数映射只有词条地址、没有中文名，`ChineseName` 为空时应保留项目原名，仍可使用 `Website`。

索引未命中时，最多检查四个搜索候选，只读取词条主要来源链接并解码百科的跳转地址，避免把正文中提及的其他 Mods 当成本项目。只有能核实名称或来源时返回 `McModTranslation`，否则返回 `null`。

网页搜索请求会串行调度并至少间隔两秒；百科明确返回搜索限流提示时抛出状态为 429 的 `HttpRequestException`，不会把限流误认成“没有收录”。网络错误仍抛异常，页面结构变化也可能导致没有结果。调用方应缓存成功结果，对未找到的结果使用较短有效期，不长期缓存网络失败，并在查询失败时保留原名。

只有网页回退会请求外部页面；方法不保存文件，单页最大 4 MiB。此方法不会执行全站采集。

## LocalModManager.EnumerateFiles

只列出目录里的 Mods 文件，返回按文件名排序的完整路径列表 `IReadOnlyList<string>`。适合先显示文件列表，再逐个调用 `ReadAsync` 补充详情。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

string directory = @"D:\Minecraft\mods";
IReadOnlyList<string> paths = await Task.Run(() => LocalModManager.EnumerateFiles(directory));
foreach (string path in paths)
    Console.WriteLine(Path.GetFileName(path));

foreach (string path in paths)
{
    try
    {
        LocalModInfo mod = await Task.Run(() => LocalModManager.ReadAsync(path));
        Console.WriteLine($"{mod.Name} {mod.Version}");
    }
    catch (InvalidDataException)
    {
        Console.WriteLine($"文件损坏：{path}");
    }
}
```

`directory` 为要读取的目录，相对路径按当前工作目录解析。只枚举直接文件，包含 `.jar`、`.litemod` 及其 `.disabled`、`.disable` 形式；不递归子目录。目录不存在时返回空列表。

`cancellationToken` 可以取消枚举，取消时抛出 `OperationCanceledException`。路径无效、目录无法枚举时，可能抛出 `ArgumentException`、`IOException` 或 `UnauthorizedAccessException`。

此方法不打开归档、不计算哈希或指纹、不请求网络，也不创建或修改文件。文件名列表不代表文件已通过校验；后续读取时仍需处理损坏、被删除或不可访问的文件。枚举也需要访问磁盘，桌面程序应在后台调用。

## LocalModManager.ScanAsync

扫描指定目录的直接 Mods 文件，返回 `IReadOnlyList<LocalModInfo>`。不递归子目录；目录不存在时返回空列表，不创建目录。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

var mods = await Task.Run(() => LocalModManager.ScanAsync(@"D:\Minecraft\mods"));
foreach (var mod in mods)
    Console.WriteLine($"{mod.Name}，启用={mod.Enabled}，错误={mod.Error}");
```

识别 `.jar`、`.litemod` 及其 `.disabled`、`.disable` 形式。读取 Fabric、Quilt、Forge、NeoForge、旧版 `mcmod.info` 与 LiteLoader 元数据；没有受支持元数据且 Manifest 也未提供名称的普通归档保留文件名。

单个文件损坏、超限或不可读时，结果保留该文件并填写 `Error`；不要将这些项从管理列表中隐藏。无法枚举目录的权限与 I/O 错误则会抛出。扫描包含磁盘读取和指纹计算，桌面应用应像示例一样放在后台执行。

## LocalModManager.ReadAsync

读取一个文件，返回元数据、启用状态、SHA-1、CurseForge MurmurHash2 指纹及可选的图标字节。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

LocalModInfo mod = await Task.Run(() =>
    LocalModManager.ReadAsync(@"D:\Minecraft\mods\example.jar"));
Console.WriteLine($"{mod.Name} {mod.Version}，{mod.Sha1}");
```

NeoForge 图标支持 `iconFile`，并兼容 Forge 的 `logoFile`。没有常规 Mods 名称时，使用 Manifest 主段中的 `Specification-Title`；版本缺失或仍为模板占位符时，使用 `Implementation-Version`，其次为 `Specification-Version`。不把构建任务名 `Implementation-Title` 作为 Mods 名。

`path` 是完整 Mods 路径，不会被修改。与扫描不同，单文件读取失败会抛出异常，不会转换成带 `Error` 的结果。

文件最大 512 MiB，ZIP 最多 100000 个条目，单份元数据和图标最多 2 MiB。超过限制或损坏时抛出 `InvalidDataException`；JSON 不合法时可能抛出 `JsonException`。图标是未解码的原始字节，调用方仍需限制图片尺寸并处理不支持的格式。

指纹按 CurseForge 规则移除制表符、换行、回车与空格字节后计算，不是将 JAR 当成文本解析。方法不执行归档中的代码。

## LocalModManager.InstallAsync

把一组本地 Mods 复制到指定目录，返回新文件的完整路径列表。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

IReadOnlyList<string> installed = await LocalModManager.InstallAsync(
    @"D:\Minecraft\mods",
    new[] { @"D:\Downloads\example.jar", @"D:\Downloads\another.jar" });
foreach (var path in installed) Console.WriteLine(path);
```

`directory` 是目标 Mods 目录；不存在时会创建。`sourceFiles` 是源文件路径列表，支持与扫描相同的扩展名，不会删除源文件。

先检查整批文件名和目标冲突，再复制到目标目录内的临时子目录，最后发布。遇到同名或对应的禁用文件时抛出 `IOException`，不会覆盖。安装失败或取消会尝试撤回本次发布的文件并清理临时目录，不移除原有文件；磁盘或权限故障也可能让清理失败，应保留异常供用户排查。

这里只复制文件，不判断 Mods 间兼容性，不解析远端依赖，也不保证选中的归档可运行。不支持的扩展名抛出 `InvalidDataException`。目标为链接目录时拒绝修改。

## LocalModManager.SetEnabled

通过重命名扩展名启用或禁用一个 Mods，返回操作后的完整路径。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

string disabled = LocalModManager.SetEnabled(
    @"D:\Minecraft\mods", @"D:\Minecraft\mods\example.jar", enabled: false);
string enabled = LocalModManager.SetEnabled(@"D:\Minecraft\mods", disabled, enabled: true);
Console.WriteLine(enabled);
```

`directory` 是所属目录，`path` 必须是它的直接 Mods 文件。禁用时添加 `.disabled`；启用时移除 `.disabled` 或 `.disable`。已经处于所需状态时返回原路径。

文件不存在抛出 `FileNotFoundException`；目标已存在、文件占用或权限不足会抛出 I/O 或权限异常。不会覆盖另一文件，不会结束正在运行的游戏。

## LocalModManager.Delete

永久删除指定目录中的一个 Mods 文件，没有返回值。

```csharp
using ErhioLab.Craft.Launcher.Core.Mods;

// 在调用前由你的应用确认用户要删除这个文件.
LocalModManager.Delete(@"D:\Minecraft\mods", @"D:\Minecraft\mods\example.jar.disabled");
```

`path` 必须是 `directory` 的直接 Mods 文件，否则抛出 `ArgumentException`。不进入子目录，不删除整个 Mods 目录，不移入回收站。不存在的文件不报错；文件占用或权限问题会抛出异常。确认与批量操作提示由调用方负责。

## 返回类型

| 类型 | 主要信息 |
| --- | --- |
| `ModProject` | 平台、项目 ID、slug、名称、简介、来源网页、图标 URL、下载量、时间、已知版本、加载器、分类及第三方下载许可。 |
| `ModFile` | 平台、项目与文件 ID、显示名、实际文件名、下载 URL、大小、SHA-1／SHA-512、发布时间、发布类型、游戏版本、加载器和必需依赖。 |
| `ModDownloadItem` | 依赖计划中的项目与明确文件。 |
| `LocalModInfo` | 路径、文件名、名称、版本、描述、Mods ID、加载器、启用状态、指纹、图标及解析错误。 |
| `McModTranslation` | 已核实的中英文名称、百科词条 URL，以及能读到的两个平台 slug。 |

`ModFile.ReleaseType` 为 `release`、`beta` 或 `alpha`。上游没有提供的信息可能为空，不应根据文件名猜造发布日期或项目身份。

## 错误处理

网络方法使用调用方的超时与取消令牌。常见异常为 `HttpRequestException`、`JsonException`、`InvalidDataException` 和 `OperationCanceledException`；搜索多个来源全部失败时使用 `AggregateException`。

API 元数据限制为 32 MiB，超过时拒绝解析。调用方应把网络故障、没有匹配项、下载被作者限制和主动取消分别展示；不要把 API Key、令牌或完整敏感响应写入日志。

以上代码示例经过编译检查。网络能力另有真实请求验证，但服务可用性、项目文件和百科页面仍会随上游变化。
