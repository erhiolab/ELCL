# 本地世界与备份

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

`LocalMinecraftWorldManager` 管理 Java 版的本地存档。不需要联网，也不依赖任何启动器配置。调用方指定 `saves` 和独立的备份根目录，并负责在操作前确认游戏已退出。每个方法都接受可选的 `CancellationToken`。

## LocalMinecraftWorldManager.ScanAsync

传入存档根目录。返回可识别的直接子目录，按上次游玩时间倒序排列；不存在的根目录返回空列表。扫描不会创建或修改存档。一个世界的 `level.dat` 读不了时会尝试 `level.dat_old`；两份都无法读取时，仍会返回能识别出的存档目录及 `MetadataError`。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string saves = Path.Combine(Environment.CurrentDirectory, "saves");
foreach (var world in await LocalMinecraftWorldManager.ScanAsync(saves))
    Console.WriteLine($"{world.Name}：{world.GameMode}，{world.Size} 字节");
```

| 属性 | 含义 |
| --- | --- |
| `DirectoryPath`、`Name` | 存档的绝对路径与世界名称；未读到名称时使用目录名。 |
| `GameMode`、`LastPlayed`、`GameVersion` | 存档声明的游戏模式、上次游玩时间及游戏版本；缺失时为 `null`。 |
| `Seed`、`Difficulty`、`AllowCommands`、`DataVersion` | 种子、难度、是否允许命令以及数据版本；缺失时为 `null`。种子兼容旧版 `level.dat` 和新版独立世界生成设置。 |
| `Size`、`Icon` | 文件总字节数与可选的 `icon.png` 字节；不可读或超过图标限制时为 `null`。 |
| `MetadataError`、`UsedPreviousMetadata` | 元数据读取错误，或成功使用 `level.dat_old` 的标记。 |

扫描跳过链接目录；根目录无法访问或是链接时抛出 `IOException` 或 `UnauthorizedAccessException`，取消时抛出 `OperationCanceledException`。

## LocalMinecraftWorldManager.ImportAsync

传入目标世界根目录、已有 Java 版世界的目录或 ZIP 路径，以及可选的取消令牌。返回新世界目录的完整路径。ZIP 可以是 `BackupAsync` 或 `ExportBackupAsync` 生成的备份，也可以是普通世界 ZIP；世界文件必须位于 ZIP 根目录或一层子目录，且只能包含一个世界。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

// 将准备好的 world-backup.zip 放在当前目录，在游戏退出后执行。
string saves = Path.Combine(Environment.CurrentDirectory, "saves");
string source = Path.Combine(Environment.CurrentDirectory, "world-backup.zip");
if (File.Exists(source))
    Console.WriteLine(await LocalMinecraftWorldManager.ImportAsync(saves, source));
```

导入复制而不移动源文件，不复制会话锁。同名时给新目录加编号，不覆盖已有世界。文件先写入临时目录，验证世界信息后才就位。ZIP 最多允许 50 万个条目和 64 GiB 解压数据；越界文件名、链接、CRC 错误、多世界 ZIP 或无法读取的世界信息会导致导入失败。路径、权限或世界锁出错时抛出对应异常；取消和检查失败不会留下未完成的世界条目。导入不转换世界格式，也不检查与目标游戏版本的兼容性。

## LocalMinecraftWorldManager.RenameAsync

传入单个世界目录、显示名称和可选的取消令牌。不返回数据。名称去除首尾空白后必须为 1～128 个 UTF-16 代码单元，不允许控制字符。操作仅更新当前 `level.dat` 的 `Data.LevelName`，不改目录名、`level.dat_old`、其他 NBT 标签或世界备份。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

// 在游戏退出后执行。My World 是世界当前的文件夹名，不是显示名称。
string world = Path.Combine(Environment.CurrentDirectory, "saves", "My World");
if (File.Exists(Path.Combine(world, "level.dat")))
    await LocalMinecraftWorldManager.RenameAsync(world, "新的世界名称");
```

当前世界缺少或无法读取 `level.dat` 时，不会修改上一份元数据。名称无效时抛出 `ArgumentException`；世界被占用、文件无效或读写失败时抛出对应异常。写入使用原子替换，取消在替换前生效。

## LocalMinecraftWorldManager.ListBackupsAsync

传入备份根目录与世界文件夹名，返回该世界的 ZIP 备份，按文件修改时间倒序排列。每项包含 `FileName`、`CreatedAt` 和 `Size`；此方法不打开 ZIP 校验内容，也不修改文件。没有备份时返回空列表。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string backups = Path.Combine(Environment.CurrentDirectory, "world-backups");
foreach (var backup in await LocalMinecraftWorldManager.ListBackupsAsync(backups, "My World"))
    Console.WriteLine($"{backup.FileName} — {backup.CreatedAt.ToLocalTime():g}");
```

世界文件夹名必须是单个名称，不是路径。无效名称、链接、权限错误或取消会抛出对应异常。

## LocalMinecraftWorldManager.BackupAsync

传入存档根目录、备份根目录与世界文件夹名。备份目录不能与存档目录重叠。返回新建的 `MinecraftWorldBackup`；ZIP 写入备份根目录下该世界的子目录。归档包含世界文件与空目录，不包含 `session.lock`，不会覆盖已有备份。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

// 先退出游戏，并确认当前目录下有 saves/My World。
string saves = Path.Combine(Environment.CurrentDirectory, "saves");
string backups = Path.Combine(Environment.CurrentDirectory, "world-backups");
var backup = await LocalMinecraftWorldManager.BackupAsync(saves, backups, "My World");
Console.WriteLine(Path.Combine(backups, "My World", backup.FileName));
```

遇到会话锁、链接、无权访问或文件错误会失败。未完成的 ZIP 不会出现在备份列表中。目录已创建但写入失败时，空目录可能保留。

## LocalMinecraftWorldManager.DeleteBackupAsync

传入备份根目录、世界文件夹名、备份 ZIP 的文件名和可选取消令牌。成功时永久删除这一份备份，不返回数据，也不修改存档或同目录下的其他 ZIP。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string backups = Path.Combine(Environment.CurrentDirectory, "world-backups");
Console.Write("输入要永久删除的备份文件名，留空则取消：");
if (Console.ReadLine() is { Length: > 0 } file)
    await LocalMinecraftWorldManager.DeleteBackupAsync(backups, "My World", file);
```

无效文件名、非 ZIP、链接、备份不存在、拒绝访问或取消时抛出相应异常。调用方应在执行前提示删除不可撤销。

## LocalMinecraftWorldManager.ExportBackupAsync

传入备份根目录、世界文件夹名、备份 ZIP 文件名和目标文件路径。可选 `overwrite` 默认 `false`；目标已存在时不覆盖。目标父目录必须已存在，目标不能位于备份根目录内。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string backups = Path.Combine(Environment.CurrentDirectory, "world-backups");
var available = await LocalMinecraftWorldManager.ListBackupsAsync(backups, "My World");
if (available.FirstOrDefault() is { } backup)
    await LocalMinecraftWorldManager.ExportBackupAsync(backups, "My World", backup.FileName,
        Path.Combine(Environment.CurrentDirectory, "my-world-backup.zip"));
```

任务完成后得到 ZIP 的副本，源备份保留。文件错误、非法路径或取消时抛出对应异常；复制先写入目标旁的临时文件，不会把未完成的副本写入目标文件名。

## LocalMinecraftWorldManager.RestoreAsync

传入与备份时相同的三个参数，再传入所选备份的 `FileName`，不能传 ZIP 的外部路径。成功时替换**已有**世界目录，不删除 ZIP，也不会替调用方备份当前世界。要保留还原前的状态，请先调用 `BackupAsync`。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

// 先退出游戏。例子使用已有备份列表的第一项。
string saves = Path.Combine(Environment.CurrentDirectory, "saves");
string backups = Path.Combine(Environment.CurrentDirectory, "world-backups");
const string world = "My World";
var available = await LocalMinecraftWorldManager.ListBackupsAsync(backups, world);
if (available.FirstOrDefault() is { } selected)
{
    await LocalMinecraftWorldManager.BackupAsync(saves, backups, world);
    await LocalMinecraftWorldManager.RestoreAsync(saves, backups, world, selected.FileName);
}
```

还原先在同级临时目录解压，校验 ZIP 文件的大小和 CRC，拒绝越界条目、链接、冲突文件和无法读取的存档元数据。校验失败或在替换前取消不会改动原世界；新目录无法就位时尝试恢复原目录。失败会抛出 `InvalidDataException`、文件系统或取消异常。同级临时目录清理失败时，异常会附上它的路径。

## LocalMinecraftWorldManager.DeleteAsync

传入存档根目录、备份根目录和世界文件夹名。此方法会**永久删除**存档，以及备份根目录下此世界对应的子目录；不会移除其他世界或外部复制的 ZIP。调用方应在执行之前提醒用户并确认。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string saves = Path.Combine(Environment.CurrentDirectory, "saves");
string backups = Path.Combine(Environment.CurrentDirectory, "world-backups");
Console.Write("输入 My World 确认删除该存档与全部备份：");
if (Console.ReadLine() == "My World")
    await LocalMinecraftWorldManager.DeleteAsync(saves, backups, "My World");
```

名称、路径、会话锁或权限检查失败时抛出对应异常。取消只会在正式删除前生效；清理失败时异常会附上移出后的目录路径。此方法不经过回收站。

## LocalMinecraftWorldManager.ReadDataPackSelectionAsync

传入单个世界目录和可选取消令牌，从当前 `level.dat` 返回 `MinecraftWorldDataPackSelection`。`Enabled` 和 `Disabled` 保留世界记录的包标识和顺序；本地 ZIP 或目录的标识形如 `file/示例.zip`，`vanilla` 等内置标识也可能出现。两边都没有的包表示世界没有为它记录选择。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string world = Path.GetFullPath(Path.Combine("saves", "My World"));
var selection = await LocalMinecraftWorldManager.ReadDataPackSelectionAsync(world);
Console.WriteLine(string.Join(", ", selection.Enabled));
```

不扫描包文件，不更改世界，也不使用 `level.dat_old`。缺少文件、目录或访问权限时抛出对应异常；NBT 或选择记录损坏时抛出 `InvalidDataException`。

## LocalMinecraftWorldManager.SetDataPackSelectionAsync

传入世界目录、本地包的文件名或目录名集合、状态和可选取消令牌。`true` 启用，`false` 禁用，`null` 从两份记录中移除选中包。传 `示例.zip` 而不是 `file/示例.zip`，重复的文件名只处理一次。完成时不返回数据，也不会添加、移动或删除包文件。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

// 先确保游戏已退出，且这个世界已经生成数据包选择记录。
string world = Path.GetFullPath(Path.Combine("saves", "My World"));
string file = "example.zip";
if (File.Exists(Path.Combine(world, "datapacks", file)))
    await LocalMinecraftWorldManager.SetDataPackSelectionAsync(world, [file], enabled: true);
```

只原子替换 `level.dat` 中的两份数据包选择记录；其他 NBT 标签及 `level.dat_old` 不变。不检查包内容或兼容性。游戏正在使用存档、存档变化、路径错误、记录无效或取消时抛出对应异常。不建议在没有备份的情况下移除世界生成类数据包。

## LocalMinecraftWorldManager.ResetIconAsync

传入世界目录和可选取消令牌。只删除该世界根目录中的 `icon.png`；实际删除时返回 `true`，本来就没有图标时返回 `false`。请在游戏退出后调用。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string world = Path.GetFullPath(Path.Combine("saves", "My World"));
Console.Write("输入 yes 确认移除现有图标：");
if (Console.ReadLine() == "yes")
    Console.WriteLine(await LocalMinecraftWorldManager.ResetIconAsync(world) ? "已移除" : "没有图标");
```

不会删除存档或备份。缺少世界、目标是链接、存档锁定、访问被拒绝或操作取消时抛出对应异常。
