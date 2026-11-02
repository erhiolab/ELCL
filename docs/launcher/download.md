# ELCL 下载页面

下载页使用 Core 的 `MinecraftVersionCatalogClient` 查询原版游戏版本, 使用 `MinecraftLoaderVersionClient` 查询附加加载器版本. `LauncherSettings.VersionListSource` 决定首选官方源还是镜像源; Core 接收转换后的 `MinecraftManifestSource`, 不读取配置文件.

页面通过通知条提示游戏版本列表的获取、成功和失败. 用户可以搜索版本, 点击版本行进入详情页. 返回后保留列表和搜索内容. 详情页分别显示 Forge、NeoForge、Fabric、Quilt、LiteLoader 和 OptiFine; 各分组可独立重试、展开和选择“无”.

加载器版本先显示, 日期另行异步补充. Quilt 日期分批查询, 使已取得的结果先显示. `MinecraftLoaderCompatibility.CanCombine` 用于过滤当前可组合的版本; 切换到不兼容加载器会清除旧选择. “开始下载”按钮目前尚未安装任何文件.

核心 API 和来源细节见[原版版本清单](../core/versions.md)与[附加加载器版本](../core/loaders.md).
