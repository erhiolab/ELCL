# 本地版本扫描

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

扫描标准游戏目录的 `versions` 子目录，读取安装信息。此操作不联网、不修改文件，也不检查资源完整性。

## `LocalMinecraftVersionScanner.Scan` {#localminecraftversionscanner-scan}

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string gameDirectory = Path.GetFullPath(".minecraft");
var versions = LocalMinecraftVersionScanner.Scan(
    gameDirectory,
    onFileError: (path, error) => Console.Error.WriteLine($"{path}：{error.Message}"),
    includeInvalidVersions: true);

foreach (var version in versions)
    Console.WriteLine(version.IsValid
        ? $"{version.Id} · {version.GameVersion} · {version.Loader}"
        : $"{version.Id}：{version.Error}");
```

| 参数 | 含义 |
| --- | --- |
| `gameDirectory` | 包含 `versions` 的根目录。 |
| `onFileError` | 可选，接收无法读取的文件路径与原始异常。 |
| `includeInvalidVersions` | 默认 `false`；管理界面可设为 `true`，保留错误目录。 |

优先读取目录同名 JSON，没有时尝试目录里的第一个 JSON。正常版本按发布时间排序，错误目录排在后面。

这是同步文件操作，大量版本的扫描应放在应用的后台任务中，避免阻塞 UI。

## `LocalMinecraftVersion` 字段

| 字段 | 用途 |
| --- | --- |
| `Id`、`FolderPath`、`JsonPath` | 显示名称及实际目录、元数据路径。 |
| `Type`、`ReleaseTime` | 元数据里的类型与时间。 |
| `GameVersion` | 实际 Minecraft 版本，可为空。 |
| `Loader`、`LoaderVersion` | 加载器及其版本，无法确认时为空。 |
| `Error`、`IsValid` | 元数据是否读取成功。 |

版本识别优先使用 `clientVersion`、Forge／NeoForge 的游戏参数、继承链与 Maven 坐标。元数据不足时只识别完整明确的安装命名格式，不从任意自定义名字里截取数字。

远古版本会保留完整 ID，例如 `rd-132211`、`c0.0.13a_03`、`in-20100125`、`a1.2.6` 和 `b1.8.1`。

## 错误条目

`Error` 为 `MissingMetadata`、`InvalidMetadata` 或 `UnreadableMetadata` 时，条目名称使用文件夹名，版本字段可能为空；`JsonPath` 也可能指向不存在的文件。

可以用它们显示错误、打开目录或提供[目录管理](version-directories.md)，但不要启动或登记为有效安装。`IsValid` 只表示元数据读取成功，启动前仍需[校验依赖](launch.md)。
