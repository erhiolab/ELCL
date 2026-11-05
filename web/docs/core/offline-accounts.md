# 离线玩家 UUID

`OfflinePlayerId.FromName` 根据玩家名生成 Minecraft Java 离线模式使用的 UUID。相同名字总会得到相同的 UUID；大小写不同，结果也不同。

```csharp
using ErhioLab.Craft.Launcher.Core.Accounts;

Guid uuid = OfflinePlayerId.FromName("Steve");
Console.WriteLine(uuid);
```

这个方法按 Java 的 `UUID.nameUUIDFromBytes` 规则，使用 `OfflinePlayer:` 加玩家名计算。传入空字符串或只有空白字符会抛出 `ArgumentException`。用户名长度和允许的字符由你的启动器决定；Core 只负责计算 UUID。

离线 UUID 不验证玩家是否拥有这个名字，也不提供 Microsoft 登录、令牌或皮肤。账户列表、当前使用的账户和账户文件都应由调用方管理。
