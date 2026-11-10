# 离线玩家 UUID

命名空间：`ErhioLab.Craft.Launcher.Core.Accounts`。

离线 UUID 是根据名字计算的身份标识，不验证游戏购买、名字所有权或 Microsoft 会话。

## `OfflinePlayerId.FromName` {#offlineplayerid-fromname}

```csharp
using ErhioLab.Craft.Launcher.Core.Accounts;

Guid uuid = OfflinePlayerId.FromName("Steve");
Console.WriteLine(uuid);
```

`name` 是原样参与计算的玩家名。算法与 Java 的 `UUID.nameUUIDFromBytes` 规则对应，使用 `OfflinePlayer:` 加名字作为输入。

相同名字总是得到相同 UUID，大小写不同则结果不同。空字符串或全空白抛出 `ArgumentException`；名字长度、允许字符和去除首尾空格由你的应用决定。

核心不保存账户列表、不选择当前账户，也不为离线 UUID 提供皮肤服务。生成启动信息的例子见[启动准备](launch.md)。
