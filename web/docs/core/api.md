# 公开 API 索引

按功能查找方法，点击名称进入说明与调用示例。这里只列公开操作，返回对象与字段见各页面。

构造方法和资源释放说明也在对应页面；已弃用的 Wiki 地址拼接方法保留迁移说明。

## 下载器

[阅读本篇](downloader.md)

- [`DownloadManager.Enqueue`](downloader.md#downloadmanager-enqueue)
- [`DownloadTaskHandle.Cancel`](downloader.md#downloadtaskhandle-cancel)
- [`DownloadManager.DisposeAsync`](downloader.md#downloadmanager-disposeasync)

## Java 发现

[阅读本篇](java.md)

- [`JavaDiscovery.FindAsync`](java.md#javadiscovery-findasync)
- [`JavaDiscovery.ProbeAsync`](java.md#javadiscovery-probeasync)
- [`JavaDiscovery.FindExhaustiveAsync`](java.md#javadiscovery-findexhaustiveasync)

## 启动模板替换标记

[阅读本篇](launch-placeholders.md)

- [`MinecraftLaunchPlaceholders.Expand`](launch-placeholders.md#minecraftlaunchplaceholders-expand)
- [`MinecraftLaunchPlaceholders.GetUnresolvedTokens`](launch-placeholders.md#minecraftlaunchplaceholders-getunresolvedtokens)

## 准备启动 Minecraft

[阅读本篇](launch.md)

- [`MinecraftLaunchVersion.LoadAsync`](launch.md#minecraftlaunchversion-loadasync)
- [`MinecraftLaunchVersion.IsJavaCompatible`](launch.md#minecraftlaunchversion-isjavacompatible)
- [`MinecraftLaunchFiles.Plan`](launch.md#minecraftlaunchfiles-plan)
- [`MinecraftLaunchFiles.ExtractNativesAsync`](launch.md#minecraftlaunchfiles-extractnativesasync)
- [`MinecraftLaunchFiles.MaterializeLegacyAssetsAsync`](launch.md#minecraftlaunchfiles-materializelegacyassetsasync)
- [`MinecraftLaunchBuilder.CreateStartInfo`](launch.md#minecraftlaunchbuilder-createstartinfo)
- [`MinecraftLaunchBuilder.CreateContext`](launch.md#minecraftlaunchbuilder-createcontext)
- [`MinecraftLaunchBuilder.SplitArguments`](launch.md#minecraftlaunchbuilder-splitarguments)

## 启动器安装档案

[阅读本篇](launcher-profiles.md)

- [`MinecraftLauncherProfilesStore.EnsureCreatedAsync`](launcher-profiles.md#minecraftlauncherprofilesstore-ensurecreatedasync)
- [`MinecraftLauncherProfilesStore.UpsertProfileAsync`](launcher-profiles.md#minecraftlauncherprofilesstore-upsertprofileasync)
- [`MinecraftLauncherProfilesStore.UpsertProfilesAsync`](launcher-profiles.md#minecraftlauncherprofilesstore-upsertprofilesasync)
- [`MinecraftLauncherProfilesStore.RemoveProfileAsync`](launcher-profiles.md#minecraftlauncherprofilesstore-removeprofileasync)

## 安装模组加载器

[阅读本篇](loader-installation.md)

- [`MinecraftLoaderInstaller.DefaultVersionName`](loader-installation.md#minecraftloaderinstaller-defaultversionname)
- [`MinecraftLoaderInstaller.ValidateSelections`](loader-installation.md#minecraftloaderinstaller-validateselections)
- [`MinecraftLoaderInstaller.PrepareAsync`](loader-installation.md#minecraftloaderinstaller-prepareasync)
- [`MinecraftLoaderInstaller.RunProcessorsAsync`](loader-installation.md#minecraftloaderinstaller-runprocessorsasync)
- [`MinecraftLoaderInstaller.WriteVersionAsync`](loader-installation.md#minecraftloaderinstaller-writeversionasync)

## 模组加载器版本

[阅读本篇](loaders.md)

- [`MinecraftLoaderVersionClient.GetVersionsAsync`](loaders.md#minecraftloaderversionclient-getversionsasync)
- [`MinecraftLoaderVersionClient.GetForgeReleaseTimesAsync`](loaders.md#minecraftloaderversionclient-getforgereleasetimesasync)
- [`MinecraftLoaderVersionClient.GetForgeVersionDatesAsync`](loaders.md#minecraftloaderversionclient-getforgeversiondatesasync)
- [`MinecraftLoaderVersionClient.GetFabricVersionDatesAsync`](loaders.md#minecraftloaderversionclient-getfabricversiondatesasync)
- [`MinecraftLoaderVersionClient.GetQuiltVersionDatesAsync`](loaders.md#minecraftloaderversionclient-getquiltversiondatesasync)
- [`MinecraftLoaderCompatibility.CanCombine`](loaders.md#minecraftloadercompatibility-cancombine)

## 本地版本扫描

[阅读本篇](local-versions.md)

- [`LocalMinecraftVersionScanner.Scan`](local-versions.md#localminecraftversionscanner-scan)

## 日志

[阅读本篇](logging.md)

- [`SessionFileLogger.Debug`](logging.md#sessionfilelogger-debug)
- [`SessionFileLogger.Information`](logging.md#sessionfilelogger-information)
- [`SessionFileLogger.Warning`](logging.md#sessionfilelogger-warning)
- [`SessionFileLogger.Error`](logging.md#sessionfilelogger-error)
- [`SessionFileLogger.Write`](logging.md#sessionfilelogger-write)
- [`SessionFileLogger.Dispose`](logging.md#sessionfilelogger-dispose)

## Microsoft 账户登录

[阅读本篇](microsoft-auth.md)

- [`MicrosoftMinecraftAuthClient.StartDeviceAuthorizationAsync`](microsoft-auth.md#microsoftminecraftauthclient-startdeviceauthorizationasync)
- [`MicrosoftMinecraftAuthClient.CompleteDeviceAuthorizationAsync`](microsoft-auth.md#microsoftminecraftauthclient-completedeviceauthorizationasync)
- [`MicrosoftMinecraftAuthClient.RefreshAsync`](microsoft-auth.md#microsoftminecraftauthclient-refreshasync)
- [`MicrosoftMinecraftAuthClient.ValidateMinecraftSessionAsync`](microsoft-auth.md#microsoftminecraftauthclient-validateminecraftsessionasync)

## 离线玩家 UUID

[阅读本篇](offline-accounts.md)

- [`OfflinePlayerId.FromName`](offline-accounts.md#offlineplayerid-fromname)

## 皮肤与披风

[阅读本篇](skins.md)

- [`MinecraftSkinClient.GetActiveSkinAsync`](skins.md#minecraftskinclient-getactiveskinasync)
- [`MinecraftSkinClient.GetAppearanceAsync`](skins.md#minecraftskinclient-getappearanceasync)
- [`MinecraftSkinClient.DownloadSkinAsync`](skins.md#minecraftskinclient-downloadskinasync)
- [`MinecraftSkinClient.DownloadCapeAsync`](skins.md#minecraftskinclient-downloadcapeasync)
- [`MinecraftSkinClient.UploadSkinAsync`](skins.md#minecraftskinclient-uploadskinasync)
- [`MinecraftSkinClient.SetActiveCapeAsync`](skins.md#minecraftskinclient-setactivecapeasync)

## JSON 存储

[阅读本篇](storage.md)

- [`JsonFileStore.Load`](storage.md#jsonfilestore-load)
- [`JsonFileStore.Save`](storage.md#jsonfilestore-save)

## 安装原版 Minecraft

[阅读本篇](vanilla-installation.md)

- [`MinecraftVanillaDownloadPlanner.ValidateVersionName`](vanilla-installation.md#minecraftvanilladownloadplanner-validateversionname)
- [`MinecraftVanillaDownloadPlanner.VersionMetadata`](vanilla-installation.md#minecraftvanilladownloadplanner-versionmetadata)
- [`MinecraftVanillaDownloadPlanner.ParseVersion`](vanilla-installation.md#minecraftvanilladownloadplanner-parseversion)
- [`MinecraftVanillaDownloadPlanner.ParseAssets`](vanilla-installation.md#minecraftvanilladownloadplanner-parseassets)
- [`MinecraftVanillaDownloadPlanner.MissingFilesAsync`](vanilla-installation.md#minecraftvanilladownloadplanner-missingfilesasync)
- [`MinecraftVanillaDownloadPlanner.WriteInstalledVersionAsync`](vanilla-installation.md#minecraftvanilladownloadplanner-writeinstalledversionasync)

## 版本目录管理

[阅读本篇](version-directories.md)

- [`MinecraftVersionDirectory.GetReferences`](version-directories.md#minecraftversiondirectory-getreferences)
- [`MinecraftVersionDirectory.RenameAsync`](version-directories.md#minecraftversiondirectory-renameasync)
- [`MinecraftVersionDirectory.Delete`](version-directories.md#minecraftversiondirectory-delete)

## 游戏版本列表

[阅读本篇](versions.md)

- [`MinecraftVersionCatalogClient.GetCatalogAsync`](versions.md#minecraftversioncatalogclient-getcatalogasync)
- [`MinecraftVersionCatalogClient.Parse`](versions.md#minecraftversioncatalogclient-parse)

## Minecraft Wiki

[阅读本篇](wiki.md)

- [`MinecraftWikiClient.GetSummaryAsync`](wiki.md#minecraftwikiclient-getsummaryasync)
- [`MinecraftWikiClient.GetChangelogAsync`](wiki.md#minecraftwikiclient-getchangelogasync)
- [`MinecraftWikiClient.GetLanguagesAsync`](wiki.md#minecraftwikiclient-getlanguagesasync)
- [`MinecraftWikiClient.GetPageUrlAsync`](wiki.md#minecraftwikiclient-getpageurlasync)
- [`MinecraftWikiClient.GetPageUrl`](wiki.md#minecraftwikiclient-getpageurl)
