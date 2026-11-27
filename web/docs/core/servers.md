# 服务器列表与状态

`MinecraftServerList` 操作 Java 版的 `servers.dat`。传入游戏工作目录；如果游戏按版本隔离，就是该版本的目录。保存前请先结束正在使用同一目录的游戏，避免游戏退出时覆盖列表。状态查询是单独的网络操作，不会更改列表。

## MinecraftServerList.ReadAsync

传入 `versionDirectory` 和可选的取消令牌，按文件原有顺序返回 `IReadOnlyList<MinecraftSavedServer>`。`Index` 是文件中的位置，`IsHidden` 表示游戏隐藏的条目；可在自己的列表中跳过它。`Icon` 是可信尺寸范围内的 PNG 数据，也可能为空。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string versionDirectory = Path.GetFullPath("game/versions/MyVersion");
foreach (var server in await MinecraftServerList.ReadAsync(versionDirectory))
    if (!server.IsHidden) Console.WriteLine($"{server.Name}：{server.Address}");
```

目录必须存在，文件不存在时返回空列表，不创建文件。不接受链接目录或链接文件；损坏、超过 16 MiB 或超过 4096 条的列表会抛出 `InvalidDataException`。读取权限和路径问题会抛出相应的 I/O 异常。

## MinecraftServerList.AddAsync

传入目录、`name`、`address` 和可选的取消令牌，返回新增的 `MinecraftSavedServer`。名称去除首尾空白后限 1～128 个字符，不接受控制字符；地址必须能由 `MinecraftServerAddress.TryParse` 解析。无效输入抛出 `ArgumentException`。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string versionDirectory = Path.GetFullPath("game/versions/MyVersion");
var added = await MinecraftServerList.AddAsync(versionDirectory, "朋友的服务器", "example.com:25566");
Console.WriteLine(added.Index);
```

只修改本地文件，不联系服务器。文件不存在时创建；已有列表不去重。损坏的文件不会被静默清空。修改使用同目录临时文件后替换，权限、磁盘、文件变化检查和取消都可能使写入失败。

## MinecraftServerList.RenameAsync

传入目录、刚从列表读取的 `server`、`name` 和可选的取消令牌。返回改名后的条目，不修改地址、资源包偏好、缓存图标或其他标签。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string versionDirectory = Path.GetFullPath("game/versions/MyVersion");
var servers = await MinecraftServerList.ReadAsync(versionDirectory);
if (servers.FirstOrDefault(server => !server.IsHidden) is { } first)
    Console.WriteLine((await MinecraftServerList.RenameAsync(versionDirectory, first, "新的名称")).Name);
```

名称规则与 `AddAsync` 相同。如果条目的索引、名称、地址或隐藏状态已经变化，抛出 `IOException`，应刷新并重新选择。写入与取消规则同 `AddAsync`。

## MinecraftServerList.RemoveAsync

传入目录、刚读取的 `server` 和可选的取消令牌。只删除一条本地记录，无返回值。这不会联系服务器，也不会删除世界、资源包或其他游戏文件。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

string versionDirectory = Path.GetFullPath("game/versions/MyVersion");
var servers = await MinecraftServerList.ReadAsync(versionDirectory);
if (servers.FirstOrDefault(server => server.Address == "example.com:25566" && !server.IsHidden) is { } old)
    await MinecraftServerList.RemoveAsync(versionDirectory, old);
```

需要确认时由调用方先询问。条目已经变化会抛出 `IOException`；写入与取消规则同 `AddAsync`。

## MinecraftServerAddress.TryParse

传入地址字符串，并通过 `out address` 获取解析结果。支持域名、IPv4、IPv6 与显式端口，未指定端口时为 `25565`。输入为空或格式无效时返回 `false`，不解析 DNS，也不建立连接。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

if (MinecraftServerAddress.TryParse("[::1]:25566", out var address))
    Console.WriteLine(address.Port);
```

## MinecraftServerAddress.ToString

无参数，返回可交给游戏的地址字符串。IPv6 会加上方括号；只有指定过端口时才附上端口。不会联网。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

if (MinecraftServerAddress.TryParse("[::1]:25566", out var address))
    Console.WriteLine(address.ToString());
```

## MinecraftServerStatusClient.PingAsync

传入 `address`，可选传入 `resolveRedirect`、`timeout` 和 `cancellationToken`。默认最多等待 5 秒。调用方可用 `resolveRedirect` 提供 DNS SRV 等重定向地址；返回 `null` 时直接连接原地址。握手仍使用原地址，避免把虚拟主机名换掉。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

// 先启动一个启用状态查询的本地 Java 版服务器。
if (MinecraftServerAddress.TryParse("localhost:25565", out var address))
{
    var status = await MinecraftServerStatusClient.PingAsync(address, timeout: TimeSpan.FromSeconds(3));
    Console.WriteLine($"{status.Version}，在线人数：{status.OnlinePlayers}，延迟：{status.Latency?.TotalMilliseconds:0} ms");
}
```

返回 `MinecraftServerStatus`，包括版本名称、在线和最大人数、带样式的描述、64×64 PNG 图标，以及往返延迟。服务器未提供的字段可能为空；只收到状态但没收到匹配的 Pong 时，返回已知状态，延迟为空。

只支持 Java 版现代 TCP 状态协议，不发送登录数据，也不写入磁盘。网络故障、异常响应、超时或取消分别可能抛出 `SocketException`／`IOException`、`InvalidDataException`／`JsonException`、`TimeoutException` 或 `OperationCanceledException`。
