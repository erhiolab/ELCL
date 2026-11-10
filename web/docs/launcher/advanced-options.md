# 高级启动选项

入口：**设置 → 启动 → 高级选项**，在 Java 列表下方，默认收起。点击整个标题区域即可展开。游戏窗口标题位于上方的启动选项卡片。

这些设置会自动保存，也会随“重置启动设置”一起恢复默认值。在下一次启动游戏时应用；编辑参数和命令时只显示预览，不会立即执行。完整流程见 [启动游戏](launch.md)。

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

这里填写传给 Minecraft 的附加参数，例如 `--demo`。它与 JVM 参数不同：`-XX:` 和 `-D` 开头的 Java 选项放在 Java 虚拟机参数中。最大堆内存由内存分配设置生成，不要依靠另填 `-Xmx` 覆盖。

游戏参数也支持替换标记。它们会追加到版本自带参数之后，同名选项按启动生成规则合并。

## 启动前执行命令

这里用于保存准备在游戏启动前执行的 Windows 命令，例如：

```text
"{java}java.exe" -jar "{verpath}test.jar"
```

目录或文件名可能包含空格时，请为完整路径加上双引号。填写命令后会出现 **等待命令执行完成后再启动游戏**，默认勾选。ELCL 固定使用版本隔离，命令的工作目录是当前版本文件夹；等待模式下，命令返回非零退出码会停止启动。取消准备任务会结束仍在运行的命令进程及其子进程。

## 垃圾回收策略与兼容选项

| 选项 | 启动时的行为 |
| --- | --- |
| 垃圾回收策略 | 尽量使用 ZGC、尽量使用分代 ZGC、标准 G1GC、调优 G1GC，或不指定并自行填写参数。默认标准 G1GC。 |
| 禁用 Java 启动兼容补丁 | 跳过命令行编码兼容补丁，默认不禁用。用户配置其他 `-javaagent` 时也会自动跳过。 |
| 禁用 LWJGL 性能补丁 | 跳过 LWJGL 3.4.1 的兼容补丁，默认不禁用；其他 LWJGL 版本不会加载它。 |
| 使用高性能显卡 | 为选中的 Java 写入 Windows 高性能显卡偏好，默认开启。关闭时清除该 Java 的显卡偏好，由系统选择。相同 Java 的其他程序也会受到这个系统设置影响。 |
| 登录时验证安全证书 | 默认开启。关闭仅影响启动前的 Microsoft 账户检查和刷新请求；下载、账户管理和皮肤请求仍验证证书。 |

垃圾回收器会按实际 Java 版本选择，必要时从 ZGC 回退到 G1。选择“不指定”会保留自己填写的 GC 参数；其他预设会清除冲突的 GC 参数。标题、窗口尺寸和内存分配的规则见[启动设置](launch-settings.md)。

## 替换标记

窗口标题、JVM 参数、游戏参数和启动前命令都可以使用下表中的标记。名称区分大小写。

| 标记 | 内容 |
| --- | --- |
| `{user}`、`{uuid}`、`{login}` | 当前账户的玩家名、无连字符的小写 UUID、登录方式。 |
| `{name}`、`{version}` | 版本名称、Minecraft 版本号。 |
| `{forge}`、`{fabric}`、`{neoforge}`、`{quilt}` | 对应加载器的版本号。 |
| `{liteloader}` | 存在 LiteLoader 时为 `true`，否则为空。 |
| `{path}`、`{path_with_name}`、`{path_temp}` | 启动器目录、启动器程序路径、缓存目录。 |
| `{minecraft}`、`{java}` | 共享的 Minecraft 根目录、Java 可执行文件所在目录。 |
| `{version_path}` 或 `{verpath}` | 当前版本目录。 |
| `{version_indie}` 或 `{verindie}` | 隔离后的游戏工作目录，即当前版本文件夹，与 `{verpath}` 相同。 |
| `{date}`、`{time}` | 生成预览时的日期与时间。 |
| `{launcher_name}`、`{launcher_version}` | 启动器名称、启动器版本。 |

目录标记会保留末尾的路径分隔符，可以直接在后面接文件名。设置页预览使用 Java 列表中的第一项；实际启动时使用通过兼容性和可用性检查的 Java。没有选择账户或版本等信息时，对应内容为空；无法识别的标记在预览中会保留原样并提示，实际启动时会报错，避免带着错误参数执行命令或创建游戏进程。

部分设置也可通过 `{setup:设置名}` 读取，目前提供：

| 设置名 | 内容 |
| --- | --- |
| `GameWindowWidth`、`GameWindowHeight` | 自定义窗口宽度、高度。 |
| `CustomMemoryPercent` | 自定义内存百分比。 |
| `GamePriority` | 进程优先级，值为 `High`、`Normal` 或 `Low`。 |
| `WindowMode` | 窗口模式的配置值。 |
| `AutoAllocateMemory` | 是否自动分配内存，值为 `true` 或 `false`。 |

也支持 `${auth_player_name}`、`${auth_uuid}`、`${version_name}`、`${game_directory}`、`${library_directory}`、`${assets_root}` 这些 Minecraft 参数标记。

ELCL 当前没有自定义变量的配置入口。开发自己的启动器时，可通过[核心替换标记 API](../core/launch-placeholders.md)提供额外值。
