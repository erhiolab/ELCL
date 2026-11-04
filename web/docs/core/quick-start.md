# 快速开始

你需要 .NET 10 SDK，以及一个目标框架为 `net10.0` 的项目。目前 Core 还没有发布 NuGet 包，可以先从仓库引用项目。

在你的 `.csproj` 中加入：

```xml
<ItemGroup>
  <ProjectReference Include="../ErhioLab.Craft.Launcher.Core/ErhioLab.Craft.Launcher.Core.csproj" />
</ItemGroup>
```

相对路径按你自己的目录结构调整。只想拿 DLL，也可以先在仓库根目录运行 `dotnet build ErhioLab.Craft.Launcher.Core.slnx -c Release`，再引用构建产物。

## 先拿到游戏版本列表

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using HttpClient http = new();
var client = new MinecraftVersionCatalogClient(http);

try
{
    var catalog = await client.GetCatalogAsync(MinecraftManifestSource.Official);
    Console.WriteLine($"最新正式版：{catalog.LatestReleaseId}");

    foreach (var version in catalog.Versions.Take(5))
        Console.WriteLine($"{version.Id} · {version.Kind}");
}
catch (MinecraftVersionCatalogException error)
{
    Console.Error.WriteLine($"两个版本源都不可用：{error.Message}");
}
```

`Official` 表示先试 Mojang 官方源；请求失败、超时或内容无效时，会再试镜像。`catalog.Source` 告诉你实际用了哪个来源。需要取消时，把 `CancellationToken` 作为第二个参数传入。

拿到清单之后，可以继续看[版本字段与本地扫描](versions.md)。要下载文件，看[下载器](downloader.md)。记得复用 `HttpClient`，不要每查一次就新建一个。
