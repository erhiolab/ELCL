# 快速开始

## 环境与引用

安装 .NET 10 SDK, 在目标框架为 `net10.0` 或兼容版本的 C# 项目中引用 Core. 仓库内可用项目引用:

如果先构建 DLL, 在仓库根目录运行:

```powershell
dotnet build .\ErhioLab.Craft.Launcher.Core.slnx -c Release
```

产物位于 `artifacts/build/ErhioLab.Craft.Launcher.Core/Release/net10.0/`. 目前仓库没有提供 NuGet 包, 发布方式仍可在后续确定.

使用项目引用时, 在接入项目的 `.csproj` 中加入:

```xml
<ItemGroup>
  <ProjectReference Include="../ErhioLab.Craft.Launcher.Core/ErhioLab.Craft.Launcher.Core.csproj" />
</ItemGroup>
```

上面的相对路径以接入项目位于 `src` 目录为例; 实际路径按项目位置调整. 如果使用编译后的 DLL, 在 IDE 中添加对 `ErhioLab.Craft.Launcher.Core.dll` 的程序集引用. .NET Framework 4.x 项目不能直接引用这个 `net10.0` 类库.

## 获取 Minecraft 版本列表

```csharp
using System;
using System.Linq;
using System.Net.Http;
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient httpClient = new();
MinecraftVersionCatalogClient client = new(httpClient);

try
{
    MinecraftVersionCatalog catalog = await client.GetCatalogAsync(
        MinecraftManifestSource.Official);

    Console.WriteLine($"实际来源: {catalog.Source}");
    Console.WriteLine($"最新正式版: {catalog.LatestReleaseId}");
    foreach (MinecraftVersion version in catalog.Versions.Take(5))
    {
        Console.WriteLine($"{version.Id} | {version.Kind} | {version.ReleaseTime:O}");
    }
}
catch (MinecraftVersionCatalogException error)
{
    Console.Error.WriteLine($"两个来源都不可用: {error.InnerException}");
}
```

`Official` 表示先请求 Mojang 清单; 如果超时、请求失败或数据无效, Core 会尝试 BMCLAPI 镜像. `catalog.Source` 是实际返回数据的来源. 调用方应复用 `HttpClient`, 并在不再使用时释放它. 需要中止请求时, 将 `CancellationToken` 传给 `GetCatalogAsync`.

## 接下来

- [版本清单](versions.md): 分类、返回字段、离线 JSON 解析与错误行为.
- [加载器](loaders.md): Forge、Fabric、Quilt 等版本查询与兼容规则.
- [日志](logging.md)和[存储](storage.md): 为自己的应用选择路径并保存数据.
