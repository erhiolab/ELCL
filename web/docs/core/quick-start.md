# 快速开始

准备 .NET 10 SDK 和一个目标框架为 `net10.0` 的项目。当前从源码引用 Core，不需要引用 ELCL 的 WPF 项目。

## 引用项目

在自己的 `.csproj` 中加入项目引用，路径按实际目录调整：

```xml
<ItemGroup>
    <ProjectReference Include="../ELCL/src/ErhioLab.Craft.Launcher.Core/ErhioLab.Craft.Launcher.Core.csproj" />
</ItemGroup>
```

也可以在仓库根目录构建核心解决方案，引用得到的 DLL：

```shell
dotnet build ErhioLab.Craft.Launcher.Core.slnx -c Release
```

## 运行第一个请求

把下面的代码放进控制台项目的 `Program.cs`，运行后会打印最新正式版与实际使用的来源。

```csharp
using ErhioLab.Craft.Launcher.Core.Minecraft;

using var http = new HttpClient();
var client = new MinecraftVersionCatalogClient(http);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    var catalog = await client.GetCatalogAsync(
        MinecraftManifestSource.Official, cancellation.Token);

    Console.WriteLine($"最新正式版：{catalog.LatestReleaseId}");
    Console.WriteLine($"实际来源：{catalog.Source}");

    foreach (var version in catalog.Versions.Take(5))
        Console.WriteLine($"{version.Id} · {version.ReleaseTime:d}");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("请求已取消或超过等待时间。");
}
catch (MinecraftVersionCatalogException error)
{
    Console.Error.WriteLine(error.Message);
}
```

`Official` 表示官方优先，无法取得有效清单时再尝试镜像，不保证最终结果一定来自官方。

## 下一步

- 想列出本机的游戏：看[本地扫描](local-versions.md)。
- 想安装一个原版版本：看[原版安装流程](vanilla-installation.md)。
- 想启动已有版本：看[启动准备](launch.md)。
- 想直接下载一个文件：看[下载器](downloader.md)。

后续示例采用隐式全局引用；自建项目可启用 `ImplicitUsings`，或自行添加 `System`、`System.IO`、`System.Net.Http` 等命名空间。
