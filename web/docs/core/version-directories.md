# 版本目录管理

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

这些方法实际改动磁盘文件。调用方应先阻止同一版本的安装、补全和运行任务，按自己的界面流程确认用户意图。

以下示例的根目录和目标目录由调用方选定：

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string gameDirectory = Path.GetFullPath(".minecraft");
string versionDirectory = Path.Combine(gameDirectory, "versions", "我的版本");
CancellationToken cancellationToken = default;
```

## `MinecraftVersionDirectory.GetReferences` {#minecraftversiondirectory-getreferences}

查询其他可读取版本通过 `inheritsFrom` 或 `jar` 对目标的引用，返回引用它的版本目录名称。

```csharp
var references = MinecraftVersionDirectory.GetReferences(
    gameDirectory, versionDirectory);
foreach (string reference in references)
    Console.WriteLine(reference);
```

它不分析模组、存档或用户脚本中的引用。列表非空时，重命名和删除会拒绝执行。

## `MinecraftVersionDirectory.RenameAsync` {#minecraftversiondirectory-renameasync}

```csharp
var renamed = await MinecraftVersionDirectory.RenameAsync(
    gameDirectory, versionDirectory, "新的版本名", cancellationToken);
Console.WriteLine(renamed.FolderPath);
```

`newName` 是新目录名，不是仅用于 UI 的显示名。成功后返回更新路径的 `LocalMinecraftVersion`。

它会修改目录、同名 JSON、JSON 的 `id` 和原同名 JAR，并保留实际游戏版本信息。未知元数据字段及版本内的其他文件会保留，仅改变大小写的名称也支持。

已有同名目标不会覆盖。错误版本也可以重命名，但不会顺带修复 JSON。取消在提交前生效，提交期间不在各文件操作之间中断。

失败时尝试恢复原名称和 JSON；若恢复也失败，抛出包含两次错误的 `AggregateException`。此时应保留目录供恢复，不要立即删除。

## `MinecraftVersionDirectory.Delete` {#minecraftversiondirectory-delete}

```csharp
MinecraftVersionDirectory.Delete(gameDirectory, renamed.FolderPath);
```

删除整个目标版本目录，包括其中的存档、模组与配置，不删除根目录的 `libraries`、`assets` 或其他版本。

目标必须是 `versions` 的直接子目录。越界、不存在、链接目录或仍被引用的目录会被拒绝，权限与占用问题也会抛出异常。

核心不清理你的当前选择、独立配置索引或安装档案。重命名和删除后，请同步这些应用状态；官方档案可用[安装档案 API](launcher-profiles.md)。
