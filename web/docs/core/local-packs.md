# 本地资源包与光影包

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

`LocalMinecraftPackScanner` 可以列出本地包、读取作者描述和版本声明，或验证并复制 ZIP。调用方提供目标目录；核心不联网、不解压整个压缩包，也不更改 Minecraft 的启用顺序。

## LocalMinecraftPackScanner.Enumerate

扫描指定目录下的直接子文件夹和 ZIP，按文件名排序。目录不存在时返回空列表；不创建目录。扫描失败会抛出文件系统异常，取消时抛出 `OperationCanceledException`。在界面线程调用时，可把扫描放在后台。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string directory = Path.Combine(Environment.CurrentDirectory, "resourcepacks");
var paths = LocalMinecraftPackScanner.Enumerate(directory);
foreach (string path in paths) Console.WriteLine(Path.GetFileName(path));
```

## LocalMinecraftPackScanner.ReadAsync

传入单个包的路径、`MinecraftPackKind.ResourcePack` 或 `MinecraftPackKind.ShaderPack`，可选传入取消令牌。返回 `LocalMinecraftPack`；包损坏、读不到或类型不符时，`Error` 保存原因，调用方仍可保留这一项。无效参数与取消仍会抛出异常。此方法不写文件。

`pack.mcmeta` 支持有或没有 BOM 的 UTF‑8。读取时跳过开头的 BOM，但仍会校验 JSON。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string path = Path.Combine(Environment.CurrentDirectory, "My Pack.zip");
var pack = await LocalMinecraftPackScanner.ReadAsync(path, MinecraftPackKind.ResourcePack);
if (pack.Error is not null) Console.WriteLine(pack.Error);
else foreach (var span in pack.Description) Console.Write(span.Text);
```

| 属性 | 含义 |
| --- | --- |
| `Name`、`Author`、`Version` | 声明的名称、作者、包版本。没有名称时使用文件名，其余缺失值为 `null`。 |
| `MinecraftVersions` | 作者显式声明的 Minecraft 版本文字或列表，不根据文件名推测。 |
| `MinimumFormat`、`MaximumFormat` | 资源包声明的格式范围。支持 `pack_format`、`supported_formats` 和 `min_format`／`max_format`。 |
| `Description`、`Icon` | 描述的文本片段和可选 `pack.png` 原始字节。颜色采用 `#RRGGBB`，支持斜体、粗体、下划线和删除线；不执行文本组件中的交互。 |
| `Path`、`IsDirectory`、`Size`、`Error` | 路径、是否为文件夹、ZIP 字节数和读取错误。不会递归计算文件夹大小。 |

光影包从 `shaders/` 结构识别。包内有 `pack.mcmeta` 或 `shaders.properties` 的名称、描述等字段才补充显示；这些不是光影引擎的通用版本规则。ZIP 中识别根目录或一个包装目录下的 `shaders/`。元数据最多 128 KiB，图标最多 4 MiB；超过限制的图标不显示。

## LocalMinecraftPackScanner.ReadClientResourceFormatAsync

传入 Minecraft 客户端 JAR 路径和可选取消令牌，从 `version.json` 读取资源包格式。旧版整数和新版主／次版本都支持；JAR 不存在或没有资源格式时返回 `null`。文件无权读取或归档、JSON 损坏时抛出对应异常，不下载或更改 JAR。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string jar = Path.Combine(Environment.CurrentDirectory, "client.jar");
var format = await LocalMinecraftPackScanner.ReadClientResourceFormatAsync(jar);
if (format is { } value) Console.WriteLine($"{value.Major}.{value.Minor ?? 0}");
```

`MinecraftPackFormat.Minor` 为 `null` 表示只声明了主版本。比较范围时，最小边界的省略次版本按 0 处理，最大边界接受该主版本的任何次版本。格式规则见 [Minecraft Java 1.21.9 的技术变更](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-9)。

## LocalMinecraftPackScanner.ImportAsync

传入目标目录、待复制的 ZIP 路径集合、包类别和可选取消令牌。返回复制成功的文件数量。识别包的结构后才复制；源文件不移动，同名目标不会覆盖。不导入文件夹，不读取链接形式的包。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string root = Environment.CurrentDirectory;
string destination = Path.Combine(root, "resourcepacks");
string source = Path.Combine(root, "My Pack.zip");
int count = await LocalMinecraftPackScanner.ImportAsync(destination, [source], MinecraftPackKind.ResourcePack);
Console.WriteLine($"已添加 {count} 个资源包");
```

错误的包类型或无效压缩包抛出 `InvalidDataException`；重名、无权访问及其他文件错误抛出对应异常。取消或复制失败时清理本批产生的文件；目标目录若刚创建，可能留空。该方法不启用游戏内的包。
