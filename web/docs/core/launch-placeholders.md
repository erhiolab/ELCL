# 启动模板的替换标记

窗口标题、附加参数和启动前命令里，经常需要放进玩家名、版本号或目录。`MinecraftLaunchPlaceholders` 可以替换这些文本中的标记。它只返回字符串，不启动游戏、不执行命令，也不会读取你的账户或设置文件。

## 替换一次文本

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

var context = new MinecraftLaunchPlaceholderContext
{
    LauncherName = "My Launcher",
    LauncherVersion = "1.0.0",
    LauncherDirectory = @"D:\Minecraft",
    JavaDirectory = @"C:\Program Files\Java\jdk-21\bin",
    GameDirectory = @"D:\Minecraft\.minecraft",
    VersionDirectory = @"D:\Minecraft\.minecraft\versions\1.21.1",
    VersionName = "1.21.1",
    GameVersion = "1.21.1",
    PlayerName = "Alex",
    PlayerUuid = Guid.Parse("a295ef70-c3d6-4e65-a0de-ca0d9b9b1ca7"),
    LoginType = "Microsoft",
    Timestamp = DateTimeOffset.Now
};

string title = MinecraftLaunchPlaceholders.Expand(
    "{user} - Minecraft {version}", context);
// Alex - Minecraft 1.21.1

string command = MinecraftLaunchPlaceholders.Expand(
    "\"{java}java.exe\" -jar \"{verpath}test.jar\"", context);
```

路径由调用方选择。普通目录标记会补齐结尾的分隔符，并保留传入路径的 Windows 或 Unix 风格。没有提供的信息替换为空字符串；核心不会为缺失的 Java、版本或账户编造值。

## 普通标记

名称区分大小写，使用下表中的小写形式。

| 标记 | 上下文中的值 |
| --- | --- |
| `{launcher_name}`、`{launcher_version}` | 启动器名称和版本 |
| `{path}`、`{path_with_name}`、`{path_temp}` | 启动器目录、可执行文件路径、缓存目录 |
| `{date}`、`{time}` | `Timestamp` 的日期 `yyyy/M/d` 和时间 `HH:mm:ss` |
| `{java}` | Java 可执行文件所在目录 |
| `{minecraft}` | Minecraft 游戏目录 |
| `{version_path}`、`{verpath}` | 版本目录，两个名称等价 |
| `{version_indie}`、`{verindie}` | 实例工作目录；未传入 `GameInstanceDirectory` 时使用游戏目录 |
| `{name}`、`{version}` | 版本文件夹名称和可读的 Minecraft 版本 |
| `{forge}`、`{fabric}`、`{neoforge}`、`{quilt}` | 对应的加载器版本；没有时为空 |
| `{liteloader}` | 安装时为 `true`，未安装时为空 |
| `{user}`、`{uuid}`、`{login}` | 玩家名、无连字符的小写 UUID、调用方提供的登录方式文字 |

日期和时间使用这次上下文的 `Timestamp`，不会启动后台计时器。如果窗口标题需要更新时间，由调用方在需要时更新 `Timestamp` 并再次调用 `Expand`。

## 设置、自定义变量和额外标记

```csharp
context = context with
{
    Settings = new Dictionary<string, string?>
    {
        ["WindowWidth"] = "1280"
    },
    Variables = new Dictionary<string, string?>
    {
        ["server"] = "example.org"
    },
    AdditionalValues = new Dictionary<string, string?>
    {
        ["profile"] = "Survival"
    }
};

string text = MinecraftLaunchPlaceholders.Expand(
    "{profile}: {variable:server:localhost}, {setup:WindowWidth}", context);
// Survival: example.org, 1280
```

`Settings` 是允许读取的值列表。核心不会访问配置文件、反射你的设置对象或解密凭据；哪些值可以出现在模板里，由调用方决定。未提供的设置名会原样保留。

`{variable:name}` 找不到变量时替换为空字符串；`{variable:name:default}` 使用默认值。默认值可以包含冒号，例如 `{variable:server:localhost:25565}`。

`AdditionalValues` 的键不带花括号，按字典枚举顺序优先替换，可以增加自己的标记。

## Minecraft 的 `${name}` 参数

`${auth_player_name}`、`${auth_uuid}`、`${version_name}`、`${game_directory}`、`${library_directory}`、`${assets_root}` 会从同一上下文取得值。

其他版本清单参数通过 `NativeValues` 传入：

```csharp
context = context with
{
    NativeValues = new Dictionary<string, string?>
    {
        ["natives_directory"] = @"D:\Minecraft\natives",
        ["classpath"] = classpath
    }
};
```

`NativeValues` 可以覆盖内置的同名 Minecraft 参数。核心不会自动读取访问令牌，也不会把参数写入日志。包含凭据的替换结果应由调用方妥善处理。

## 替换顺序与未识别的标记

顺序为：额外标记 → 启动器信息和路径 → 日期时间 → Minecraft 信息 → 登录信息 → 设置及变量 → `${name}` 参数。这与 PCL 的顺序替换思路一致：前面插入的文本如果含有后面才处理的标记，会继续替换。它不做无限递归；后面插入的早期标记不会再回头处理。

未知标记保留原样，可以用下面的方法提醒用户检查：

```csharp
string result = MinecraftLaunchPlaceholders.Expand(template, context);
IReadOnlyList<string> unresolved = MinecraftLaunchPlaceholders.GetUnresolvedTokens(result);
```

返回值包含完整标记（例如 `{unknown}` 或 `${classpath}`），按出现顺序去重。PCL 专属的版本信息、识别码、回声洞等标记没有内置实现；如果自己的程序需要类似内容，可以显式提供额外值。

替换器不拆分命令行、不添加引号，也不转义 shell 字符。构造进程参数时优先使用 `ProcessStartInfo.ArgumentList`；如果明确使用 shell 命令，需要按所选 shell 的规则处理路径、引号和用户输入。
