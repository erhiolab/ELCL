# 启动器安装档案

命名空间：`ErhioLab.Craft.Launcher.Core.Minecraft`。

`launcher_profiles.json` 保存官方启动器的安装配置，不是账户文件，也不是启动游戏的必需条件。这个 API 更新指定档案，同时保留其他条目和未知字段。

## 创建 `MinecraftLauncherProfilesStore`

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string gameDirectory = Path.GetFullPath(".minecraft");
var store = new MinecraftLauncherProfilesStore(
    Path.Combine(gameDirectory, MinecraftLauncherProfilesStore.LegacyFileName));
CancellationToken cancellationToken = default;
```

构造只确定路径，不会立即创建文件。`FilePath` 返回完整路径。

Microsoft Store 版文件名常量是 `MicrosoftStoreFileName`。要同步两份文件时分别创建 Store，核心不会自动寻找另一份。

## `MinecraftLauncherProfilesStore.EnsureCreatedAsync` {#minecraftlauncherprofilesstore-ensurecreatedasync}

```csharp
bool created = await store.EnsureCreatedAsync(cancellationToken);
```

不存在时创建含空 `profiles` 与 `settings` 的文件，成功创建返回 `true`。已经存在返回 `false`，即使内容损坏也不覆盖。

## `MinecraftLauncherProfilesStore.UpsertProfileAsync` {#minecraftlauncherprofilesstore-upsertprofileasync}

按稳定 ID 新增或更新一个档案。

```csharp
string profileId = "my-launcher-1.21.1";
var profile = new MinecraftLauncherProfile(
    Name: "原版 1.21.1", VersionId: "1.21.1",
    GameDirectory: Path.Combine(gameDirectory, "versions", "1.21.1"));
await store.UpsertProfileAsync(profileId, profile,
    cancellationToken: cancellationToken);
```

| 输入 | 含义 |
| --- | --- |
| `profileId` | 调用方保存并复用的档案 ID，不要每次生成新值。 |
| `profile.Name`、`VersionId` | 显示名称和实际安装 ID，不是加载器所基于的原版 ID。 |
| `GameDirectory` | 工作目录，写入绝对路径；为空时移除该档案的 `gameDir`。 |
| `Icon` | 官方图标值，默认 `Grass`。 |
| `markUsed` | 默认 `false`；游戏成功启动后可设为 `true` 更新最后使用时间。 |

文件不存在时也可直接使用，不必先调用 `EnsureCreatedAsync`。相同 ID 更新原条目，保留 `created` 和未知字段；默认不改已有 `lastUsed`。

## `MinecraftLauncherProfilesStore.UpsertProfilesAsync` {#minecraftlauncherprofilesstore-upsertprofilesasync}

批量登记，避免每个版本单独写文件。

```csharp
var profiles = new Dictionary<string, MinecraftLauncherProfile>
{
    [profileId] = profile,
    ["my-launcher-1.21.4"] = new("原版 1.21.4", "1.21.4")
};
await store.UpsertProfilesAsync(profiles, cancellationToken: cancellationToken);
```

只新增或更新传入 ID，不删除未列出的档案。任意条目无效时整批不写入。内容没有变化时不重写文件。

不要仅凭 `VersionId` 相同覆盖其他启动器的条目，应为自己的档案分配独立 ID。

## `MinecraftLauncherProfilesStore.RemoveProfileAsync` {#minecraftlauncherprofilesstore-removeprofileasync}

```csharp
await store.RemoveProfileAsync(profileId, cancellationToken);
```

只移除指定 ID。文件或档案不存在时直接返回，不创建文件。

## 写入保护与异常

现有文件必须为 UTF-8，可带 UTF-8 BOM。无效 UTF-8、无效 JSON、重复属性、非对象根节点或非对象档案会抛出 `JsonException`，不自动修复或覆盖。GBK 等编码不会被猜测转换，以免改坏其他程序的档案。

更新或删除档案前会先严格检查整个文件的 UTF-8 编码，包括不属于调用方的条目和未知字段。编码错误的 `JsonException` 会保留原始解码异常作为 `InnerException`，调用方可以记录错误并跳过档案同步，继续使用游戏版本。

文件先写同目录临时文件再替换。同进程同路径的操作串行执行；检测到写入前文件被其他程序改变时抛出 `IOException`。这不是跨进程事务锁，仍应避免两个启动器同时修改。

格式背景见 [Minecraft Wiki](https://zh.minecraft.wiki/w/启动器档案文件格式)。
