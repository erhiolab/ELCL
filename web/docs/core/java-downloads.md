# Java 发行版本

命名空间：`ErhioLab.Craft.Launcher.Core.Java`。

这组接口用于查询可以下载的 Java，和[搜索本机 Java](java.md)分开。数据直接来自 Mojang、Adoptium、Azul 和 IBM 的上游服务，不经过其他启动器的版本源。

核心返回版本、下载地址和校验信息，不决定安装目录，也不解压、执行 Java 或保存应用设置。

## `JavaDistributionClient` 构造方法

传入你自己的 `HttpClient`。连接、代理和超时由调用方管理；核心不会释放它。以下示例共用这段初始化代码。

```csharp
using ErhioLab.Craft.Launcher.Core.Java;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
var client = new JavaDistributionClient(http);
JavaPlatform platform = JavaPlatform.Current;
CancellationToken cancellationToken = default;
```

### 支持的发行方

| `JavaDistribution` | 数据来源 |
| --- | --- |
| `Mojang` | 官方 Java runtime 目录与文件清单。 |
| `Adoptium` | Adoptium v3 API，Temurin HotSpot 正式版。 |
| `Azul` | Azul Metadata API，Zulu 社区正式版，排除 JavaFX、CRaC 与 musl 专用包。 |
| `Semeru` | IBM 的 `ibmruntimes` GitHub 组织与 Semeru Open 发行记录。 |

Windows 返回 ZIP，Linux 和 macOS 返回 tar.gz，不返回 MSI、EXE、PKG、RPM 等系统安装程序。Mojang 则返回文件清单，通常没有一个可以整体下载的 ZIP。

### 指定目标设备

`JavaPlatform.Current` 使用操作系统架构，而不是当前 .NET 进程架构。例如，在 64 位系统上运行 32 位应用时，仍查询原生 64 位 Java。

也可以明确传入目标：

```csharp
var armMac = new JavaPlatform(
    "macos", System.Runtime.InteropServices.Architecture.Arm64);
```

操作系统只接受 `windows`、`linux` 和 `macos`；架构接受 `X64`、`X86`、`Arm64` 和 `Arm`。这表示查询条件，不保证每个发行方都为它提供下载。不自动替换成其他架构。

## `GetMajorVersionsAsync` {#javadistributionclient-getmajorversionsasync}

获取一个发行方的 Java 主版本目录。结果去重并降序排列，例如 `25、21、17、8`。

```csharp
IReadOnlyList<int> majors = await client.GetMajorVersionsAsync(
    JavaDistribution.Mojang, platform, cancellationToken);
foreach (int major in majors)
    Console.WriteLine($"Java {major}");
```

| 参数 | 含义 |
| --- | --- |
| `distribution` | 要查询的发行方。 |
| `platform` | 可选，省略时使用当前设备。 |
| `cancellationToken` | 取消网络请求、分页和重试等待。 |

Mojang 和 Azul 按目标设备获取目录；Adoptium 和 IBM 先提供发行方的主版本目录，具体设备是否有可部署的包，需要继续调用 `GetVersionsAsync`。不能把目录中有某个主版本理解成一定可以下载。

每次调用都会访问上游，不内置磁盘缓存。刷新多个平台时，可以并发调用并分别处理失败，避免一个平台不可用影响全部结果。

## `GetVersionsAsync` {#javadistributionclient-getversionsasync}

查询某个平台、主版本和设备对应的具体下载版本。

```csharp
JavaVersionPage firstPage = await client.GetVersionsAsync(
    JavaDistribution.Adoptium,
    majorVersion: 21,
    platform: platform,
    page: 0,
    pageSize: 10,
    cancellationToken: cancellationToken);

foreach (JavaDownloadVersion version in firstPage.Versions)
    Console.WriteLine($"{version.Version} · {version.PackageType} · {version.DownloadUri}");
Console.WriteLine($"还有下一页：{firstPage.HasMore}");
```

`majorVersion` 必须大于 0。`page` 从 0 开始，默认 0；`pageSize` 范围 1～100，默认 20。请求下一页时保留其他参数，将 `page` 加 1。

分页单位随上游而异：Adoptium 和 IBM 按发行记录分页，一条记录可以同时包含 JRE 和 JDK；Azul 按包分页。因此，返回包数可能与 `pageSize` 不同。以 `HasMore` 判断是否继续，不要仅比较结果数量。筛选后空页也可能仍有下一页。

只返回正式版及匹配设备的可部署包。HTTP 404 作为没有适用版本处理；其他网络错误仍向调用方抛出。没有结果时不会猜测地址或改用其他架构。

### 版本字段

| 字段 | 含义 |
| --- | --- |
| `Distribution`、`MajorVersion`、`Version` | 发行方、主版本、上游完整版本号。 |
| `PackageType` | JRE 或 JDK。两者均可包含游戏需要的运行环境。 |
| `Name` | 包文件名；Mojang 使用运行时组件名称。 |
| `Platform`、`Format` | 目标设备，以及 ZIP、tar.gz 或文件清单。 |
| `DownloadUri` | 包下载地址；Mojang 为清单地址。 |
| `Size` | 上游提供的字节数，未知为 `null`；Mojang 为清单本身的大小。 |
| `ReleasedAt` | 上游提供的时间，未知为 `null`，不会用查询时间代替。 |
| `Sha256`、`Sha1` | 包或清单的校验值，部分发行方需先查询详情。 |
| `MetadataUri`、`ChecksumUri` | 补齐包详情或 SHA-256 所需的上游地址。 |

Azul 列表通常没有大小与日期，`GetPackageAsync` 会补充详情中的大小和构建日期。构建日期不保证等于网站公开发布的时间。

## `GetPackageAsync` {#javadistributionclient-getpackageasync}

在开始下载之前调用，取得足够用于校验的包信息。

```csharp
JavaDownloadVersion selected = firstPage.Versions
    .First(version => version.PackageType == JavaPackageType.Jre);
JavaDownloadPackage package = await client.GetPackageAsync(
    selected, cancellationToken);

if (package.Version.Format == JavaDownloadFormat.Manifest)
{
    foreach (JavaRuntimeEntry entry in package.Files)
        Console.WriteLine($"{entry.Type} · {entry.Path} · {entry.DownloadUri}");
}
else
{
    Console.WriteLine($"下载：{package.Version.DownloadUri}");
    Console.WriteLine($"SHA-256：{package.Version.Sha256}");
}
```

此示例接着上面的 `firstPage`，实际应用应先处理空结果和用户的选择。

- **压缩包**：确认 SHA-256 可用，必要时查询 Azul 包详情或读取 IBM 的校验文件。`Files` 为空。下载完成后，由调用方比较大小、计算 SHA-256、解压并验证 Java。
- **Mojang 清单**：下载清单并检查其大小、SHA-1，然后返回文件、目录和链接条目。文件包含原始下载地址、大小、SHA-1 和可执行标记。

`JavaRuntimeEntry.Path` 是相对路径。调用方写入磁盘时，必须确认它与链接目标都位于自己的安装目录内；其他系统还需要应用可执行权限和处理链接。核心不会替你写入这些条目。

可将普通文件条目转换成[下载器](downloader.md)的 `DownloadFileRequest`，让多个小文件并发下载；ZIP／tar.gz 则作为单个文件提交，服务器支持时由下载器分段处理。

## 失败与重试

参数错误抛出 `ArgumentException` 或 `ArgumentOutOfRangeException`；上游数据损坏、校验不匹配或缺少必需校验值时抛出 `JsonException` 或 `InvalidDataException`。网络及读取错误保留原异常，调用方取消会抛出 `OperationCanceledException`。

短暂的连接中断、超时、HTTP 408／429／5xx 最多额外重试两次；取消会停止重试。每份元数据最多读取 32 MiB。GitHub 的限流与上游维护仍可能导致查询失败，应用应保留可重试入口。

发行方接口说明：[Adoptium](https://api.adoptium.net/)、[Azul](https://docs.azul.com/core/install/metadata-api)、[IBM Semeru](https://www.ibm.com/support/pages/semeru-runtimes-getting-started)。
