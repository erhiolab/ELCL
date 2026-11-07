# 高级启动选项

在 **设置 → 启动** 中，可以修改游戏窗口标题。Java 列表下方的 **高级选项** 默认收起，点击标题即可展开。

这些设置会自动保存，也会随“重置启动设置”一起恢复默认值。目前游戏启动流程尚未接入，参数、命令和策略会先保存下来；替换标记的预览已经可以使用。填写启动前命令不会立即执行它。

## 游戏窗口标题

留空时使用游戏自己的标题，也可以填写固定文字或加入替换标记：

```text
{user} - Minecraft {version}
```

例如，当前账户为 Alex、选择的游戏版本为 1.21.1，预览会显示 `Alex - Minecraft 1.21.1`。

## Java 虚拟机参数

这里填写附加的 JVM 参数，默认内容为：

```text
-XX:-OmitStackTraceInFastThrow -Djdk.lang.Process.allowAmbiguousCommands=True -Dfml.ignoreInvalidMinecraftCertificates=True -Dfml.ignorePatchDiscrepancies=True
```

| 参数 | 用途 |
| --- | --- |
| `-XX:-OmitStackTraceInFastThrow` | 避免 JVM 为频繁抛出的部分异常省略调用堆栈，方便排查问题。 |
| `-Djdk.lang.Process.allowAmbiguousCommands=True` | 设置 Java 的进程命令行兼容选项，实际效果取决于 Java 版本。 |
| `-Dfml.ignoreInvalidMinecraftCertificates=True` | 供部分旧版 Forge 使用的兼容参数，允许忽略 Minecraft 证书检查问题。 |
| `-Dfml.ignorePatchDiscrepancies=True` | 供部分旧版 Forge 使用的兼容参数，允许忽略补丁差异检查。 |

多个参数用空格分开，也可以换行编辑。标签右侧的重置按钮只恢复默认 JVM 参数，不会重置其他设置。没有特别需求时可以保留默认值；这些兼容参数并不能解决所有版本或模组的启动问题。

## 游戏参数

这里填写传给 Minecraft 的附加参数，例如 `--demo`。它与 JVM 参数不同：`-Xmx`、`-XX:` 和 `-D` 开头的 Java 选项应放在 Java 虚拟机参数中。

游戏参数也支持替换标记。

## 启动前执行命令

这里用于保存准备在游戏启动前执行的 Windows 命令，例如：

```text
"{java}java.exe" -jar "{verpath}test.jar"
```

目录或文件名可能包含空格时，请为完整路径加上双引号。填写命令后会出现 **等待命令执行完成后再启动游戏**，默认勾选。命令执行与等待逻辑将在游戏启动流程中接入；目前只保存文本并显示替换预览。

## 内存管理与兼容选项

| 选项 | 当前保存的偏好 |
| --- | --- |
| 内存管理 | 尽量使用 ZGC、尽量使用分代 ZGC、标准 G1GC、调优 G1GC，或不指定并自行填写参数。默认标准 G1GC。 |
| 禁用 Java Launch Wrapper | 是否跳过 Java Launch Wrapper 兼容补丁，默认不禁用。 |
| 禁用 LWJGL Unsafe Agent | 是否跳过 LWJGL Unsafe Agent 兼容补丁，默认不禁用。 |
| 使用高性能显卡 | 是否优先使用高性能显卡，默认开启。 |
| 在正版登录时验证 SSL 证书 | 证书验证偏好，默认开启。现有账户登录请求仍然验证证书。 |

这些策略目前尚未应用到进程或登录请求。垃圾回收器是否可用，还取决于实际使用的 Java 版本。

## 替换标记

窗口标题、JVM 参数、游戏参数和启动前命令都可以使用下表中的标记。名称区分大小写。

| 标记 | 内容 |
| --- | --- |
| `{user}`、`{uuid}`、`{login}` | 当前账户的玩家名、无连字符的小写 UUID、登录方式。 |
| `{name}`、`{version}` | 版本名称、Minecraft 版本号。 |
| `{forge}`、`{fabric}`、`{neoforge}`、`{quilt}` | 对应加载器的版本号。 |
| `{liteloader}` | 存在 LiteLoader 时为 `true`，否则为空。 |
| `{path}`、`{path_with_name}`、`{path_temp}` | 启动器目录、启动器程序路径、缓存目录。 |
| `{minecraft}`、`{java}` | 当前 Minecraft 目录、Java 可执行文件所在目录。 |
| `{version_path}` 或 `{verpath}` | 当前版本目录。 |
| `{version_indie}` 或 `{verindie}` | 游戏工作目录。当前没有启用版本隔离，使用 Minecraft 目录。 |
| `{date}`、`{time}` | 生成预览时的日期与时间。 |
| `{launcher_name}`、`{launcher_version}` | 启动器名称、启动器版本。 |

目录标记会保留末尾的路径分隔符，可以直接在后面接文件名。当前预览使用 Java 列表中的第一项，实际启动时仍需根据游戏版本选择兼容的 Java。没有选择账户或版本等信息时，对应内容为空；无法识别的标记会保留原样，并在预览下方提示。

部分设置也可通过 `{setup:设置名}` 读取，目前提供：

| 设置名 | 内容 |
| --- | --- |
| `GameWindowWidth`、`GameWindowHeight` | 自定义窗口宽度、高度。 |
| `CustomMemoryPercent` | 自定义内存百分比。 |
| `GamePriority` | 进程优先级，值为 `High`、`Normal` 或 `Low`。 |
| `WindowMode` | 窗口模式的配置值。 |
| `AutoAllocateMemory` | 是否自动分配内存，值为 `true` 或 `false`。 |

也支持 `${auth_player_name}`、`${auth_uuid}`、`${version_name}`、`${game_directory}`、`${library_directory}`、`${assets_root}` 这些 Minecraft 参数标记。

ELCL 没有提供自定义变量的配置入口；PCL 专属的版本信息、识别码和回声洞等标记不适用。开发自己的启动器时，可以查阅 [ELCLC 的替换标记 API](../core/launch-placeholders.md)。
