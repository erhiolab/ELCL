# Java 运行时发现

`ErhioLab.Craft.Launcher.Core.Java.JavaDiscovery` 可以独立于 ELCL 设置页发现并验证本机 Java. 返回的 `JavaInstallation` 包含可执行文件路径/完整版本/主版本号和架构. Core 不保存列表/排序或用户排除项; 调用方决定这些数据如何持久化.

```csharp
using ErhioLab.Craft.Launcher.Core.Java;

IReadOnlyList<JavaInstallation> found = await JavaDiscovery.FindAsync();
JavaInstallation? selected = found.FirstOrDefault(java => java.MajorVersion >= 17);

// 安装器有自定义目录时, 只把那个根目录交给 Core.
IReadOnlyList<JavaInstallation> custom = await JavaDiscovery.FindAsync(
    additionalRoots: [@"D:\MyJdks"], cancellationToken: cancellationToken
);

// 手动添加前也应验证文件.
JavaInstallation? manual = await JavaDiscovery.ProbeAsync(@"D:\MyJdks\jdk-21\bin\java.exe");
```

查找使用 `PATH`/`JAVA_HOME`/`JDK_HOME`/常见安装目录和已发现 Java 附近的受限目录. Windows 下查找 `java.exe`, macOS/Linux 下查找 `java`; 对目录深度/访问目录数与候选数设上限, 不递归遍历整个磁盘. 探测会限时运行 `-XshowSettings:properties -version` 并解析版本. 无法运行或无法识别的候选不会出现在结果中. 搜索和探测均支持取消令牌.

Windows 注册表是平台集成. ELCL 从注册表读取安装目录, 通过 `additionalRoots` 交给 Core; 其他客户端可以提供自己的目录来源. ELCL 同时在配置中保存列表顺序和手动排除项, 详见[启动器设置](../launcher/settings.md).
