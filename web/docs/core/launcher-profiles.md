# 启动器安装档案

`launcher_profiles.json` 保存的是官方启动器中的安装配置，例如版本名称、版本 ID 和游戏目录。它不是启动 Minecraft 的必需文件，但一些安装器会检查它，也可以用它让官方启动器识别你创建的安装配置。

`MinecraftLauncherProfilesStore` 用来创建和更新这类文件。它和保存应用设置的 `JsonFileStore<T>` 用途不同：更新档案时会保留其他配置和未知字段，不会用自己的模型覆盖整个文件。

## 创建文件和登记版本

文件路径、档案 ID 和游戏目录都由调用方指定：

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string gameRoot = Path.GetFullPath(".minecraft");
string versionId = "1.21.1";
string versionDirectory = Path.Combine(gameRoot, "versions", versionId);
var store = new MinecraftLauncherProfilesStore(
    Path.Combine(gameRoot, MinecraftLauncherProfilesStore.LegacyFileName));

// 仅在文件不存在时创建。已存在则返回 false，即使原文件无法解析。
bool created = await store.EnsureCreatedAsync(cancellationToken);

// 实际应用中应保存并复用这个 ID；每次生成新 ID 会产生重复条目。
string profileId = "0f3f3584bbae4c51a1451c34b4827069";
var profile = new MinecraftLauncherProfile(
    Name: "原版 1.21.1",
    VersionId: versionId,
    GameDirectory: versionDirectory);

await store.UpsertProfileAsync(profileId, profile,
    cancellationToken: cancellationToken);
```

新建文件只包含空的 `profiles` 和 `settings` 对象。`UpsertProfileAsync` 也可以直接用于不存在的文件，无需先调用 `EnsureCreatedAsync`。

`Name` 是显示名称，`VersionId` 对应 `versions/<版本 ID>/` 中的实际版本，而不是它所基于的原版版本号。例如安装名是 `1.21.1-Fabric`，这里就应填写 `1.21.1-Fabric`。

`GameDirectory` 是该安装配置的工作目录，会保存为绝对路径。需要版本隔离时传入版本目录；省略时移除该档案的 `gameDir` 字段，让官方启动器使用自己的默认目录。是否隔离由调用方决定。`Icon` 默认为官方启动器内置的 `Grass`，也可以传入其他受支持的图标值。

## 更新和移除

同一个 ID 会更新原条目，不会增加另一份。每次更新会写入 `name`、`lastVersionId`、`type: "custom"`、`icon` 和 `gameDir`，其他字段保留。新条目会填写 UTC 格式的 `created`，未启动过的 `lastUsed` 为 `1970-01-01T00:00:00.000Z`。

游戏成功启动后，可以更新最后使用时间：

```csharp
await store.UpsertProfileAsync(profileId, profile, markUsed: true,
    cancellationToken: cancellationToken);

// 删除版本时，只移除属于自己程序的档案 ID。
await store.RemoveProfileAsync(profileId, cancellationToken);
```

更新已有条目会保留 `created`。默认也保留 `lastUsed`，只有 `markUsed: true` 才会更新它。移除不存在的档案不会写文件；文件不存在时也不会创建。

如果你的程序已经扫描到了多个已安装版本，可以一次登记，避免每个版本单独写文件：

```csharp
var profiles = new Dictionary<string, MinecraftLauncherProfile>
{
    [profileId] = profile,
    ["cc0818f351554b5ba12358fa02b426f4"] = new(
        "原版 1.21.4", "1.21.4", Path.Combine(gameRoot, "versions", "1.21.4"))
};
await store.UpsertProfilesAsync(profiles, cancellationToken: cancellationToken);
```

批量登记只增加或更新传入的 ID，不删除其他档案。任意条目无效时整批不会写入；文件内容没有变化时，单条和批量更新都不会重写文件。默认不会把扫描版本算成启动游戏，因此不改变已有的最后使用时间。

请为自己的档案使用稳定且独立的 ID，不要仅凭 `lastVersionId` 相同就覆盖其他启动器创建的条目。

## 文件兼容和写入保护

Microsoft Store 版官方启动器可能使用 `launcher_profiles_microsoft_store.json`，可以把这个路径传给同一个类，文件名常量为 `MicrosoftStoreFileName`。核心不会自行寻找或同步另一份文件。

已有文件中的其他安装配置、`settings`、账户字段和未知字段会保留；核心不会读取这些字段来登录，也不会把调用方的账户或令牌写进去。它不会替调用方选择官方启动器的当前档案。

文件无法解析、包含重复属性、根节点或 `profiles` 不是对象、待更新条目不是对象时，会抛出 `JsonException`，原文件保留。权限、占用和其他文件错误通过相应异常交给调用方处理，不会自动重置文件。

写入先落到同目录的临时文件，再替换目标文件。同一进程内，对同一路径的操作会串行执行，多个实例也共用这把锁。替换前若检测到原文件内容被其他程序改变，会抛出 `IOException`；这不是跨进程事务锁，仍应避免和官方启动器同时修改同一个文件。所有异步方法都接受取消令牌。

格式说明可参考 [Minecraft Wiki：启动器档案文件格式](https://zh.minecraft.wiki/w/启动器档案文件格式)。
