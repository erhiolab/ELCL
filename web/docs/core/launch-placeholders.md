# 启动模板替换标记

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

替换窗口标题、参数或命令文本。它只处理字符串，不读取设置、不执行命令，也不自动加引号。

## `MinecraftLaunchPlaceholders.Expand` {#minecraftlaunchplaceholders-expand}

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

var context = new MinecraftLaunchPlaceholderContext
{
    LauncherName = "MyLauncher", LauncherVersion = "1.0",
    PlayerName = "Alex", GameVersion = "1.21.1", VersionName = "日常存档",
    JavaDirectory = @"C:\Program Files\Java\jdk-21\bin",
    VersionDirectory = @"D:\Minecraft\versions\日常存档"
};
string title = MinecraftLaunchPlaceholders.Expand("{user} - {name} ({version})", context);
// Alex - 日常存档 (1.21.1)
```

`template` 是文本，`context` 提供值，返回展开结果。已知标记缺少值时替换为空，未知标记保留原样。名称区分大小写，不会无限递归。

普通目录标记会补齐末尾分隔符。日期与时间读取上下文的 `Timestamp`，不会建立计时器；实时标题需要调用方更新上下文并重新展开。

## `MinecraftLaunchPlaceholders.GetUnresolvedTokens` {#minecraftlaunchplaceholders-getunresolvedtokens}

```csharp
string result = MinecraftLaunchPlaceholders.Expand("{user} - {unknown}", context);
var unresolved = MinecraftLaunchPlaceholders.GetUnresolvedTokens(result);
Console.WriteLine(string.Join("、", unresolved));
```

返回仍存在的完整标记，按出现顺序去重，例如 `{unknown}` 或 `${classpath}`。适合在执行前提示拼写错误或缺失上下文。

## 普通标记对照

| 标记 | 上下文内容 |
| --- | --- |
| `{launcher_name}`、`{launcher_version}` | 启动器名称和版本。 |
| `{path}`、`{path_with_name}`、`{path_temp}` | 启动器目录、可执行文件、缓存目录。 |
| `{date}`、`{time}` | `Timestamp` 的 `yyyy/M/d` 与 `HH:mm:ss`。 |
| `{java}`、`{minecraft}` | Java 所在目录、共享游戏根目录。 |
| `{version_path}`、`{verpath}` | 版本目录，两者等价。 |
| `{version_indie}`、`{verindie}` | `GameInstanceDirectory`，未提供时用游戏根目录。 |
| `{name}`、`{version}` | 安装名与实际 Minecraft 版本。 |
| `{forge}`、`{fabric}`、`{neoforge}`、`{quilt}` | 对应加载器版本。 |
| `{liteloader}` | 有 LiteLoader 时为 `true`，否则为空。 |
| `{user}`、`{uuid}`、`{login}` | 玩家名、无连字符小写 UUID、调用方的登录方式文字。 |

## 设置和自定义变量

```csharp
context = context with
{
    Settings = new Dictionary<string, string?> { ["Width"] = "1280" },
    Variables = new Dictionary<string, string?> { ["server"] = "example.org" },
    AdditionalValues = new Dictionary<string, string?> { ["profile"] = "Survival" }
};
string custom = MinecraftLaunchPlaceholders.Expand(
    "{profile}：{variable:server:localhost}，{setup:Width}", context);
```

`{setup:Name}` 只访问 `Settings` 字典，不反射你的配置。不存在的设置标记保留原样。

`{variable:name}` 没有值时为空，`{variable:name:default}` 可指定默认值。默认值可以有冒号。

`AdditionalValues` 的键不带花括号，供应用定义额外普通标记，并优先于内置普通值处理。

## Minecraft 原生参数

`${auth_player_name}`、`${auth_uuid}`、`${version_name}`、`${game_directory}`、`${library_directory}` 与 `${assets_root}` 可以直接从上下文取得。

其余值由 `NativeValues` 提供。实际启动时可用[CreateContext](launch.md)补齐类路径、资源索引与会话参数。

```csharp
context = context with
{
    NativeValues = new Dictionary<string, string?>
    {
        ["classpath"] = "library-a.jar;library-b.jar"
    }
};
```

`NativeValues` 可以覆盖同名原生值。若其中含令牌，不要输出完整字典或展开后的参数。

## 顺序与转义

按额外普通值、内置普通值、设置、变量、原生参数依次处理。前面插入的后续标记可能继续展开，不会反复回头递归替换。

替换器不拆分命令行，也不转义 Shell。运行进程优先使用 `ProcessStartInfo.ArgumentList`；确实需要 Shell 时，按对应 Shell 规则处理引号与输入。

PCL 专属信息和服务标记没有内置实现，需要时由应用显式提供额外值。
