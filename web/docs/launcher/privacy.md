# 隐私与匿名统计

在“设置 → 其他 → 隐私”中可以关闭“允许匿名数据收集”。此选项默认开启，修改后立即保存，不会显示保存成功通知。关闭会取消尚未完成的统计请求，并阻止后续上报；已经发送的事件无法通过本地开关撤回。重新开启后，只采集之后发生的事件，不补报之前错过的事件。

## 收集内容

ELCL 当前向 PostHog 美国区发送以下两种事件：

| 事件 | 触发时机 |
| --- | --- |
| `launcher_opened` | 每个启动器进程启动时最多尝试一次。 |
| `game_launch_succeeded` | 确认游戏进程的可见窗口首次出现后，每次成功启动尝试一次。导出启动脚本、仅创建进程或窗口出现前失败不触发。 |

事件内容包括：

- 随机生成的匿名安装 ID，作为 `distinct_id`。
- 事件发生的 UTC 时间。
- 关闭人物档案处理、已识别用户标记和地理位置补全的控制字段。

不会发送 Microsoft 账户、玩家名称、UUID、令牌、游戏版本、加载器、游戏内容、文件路径、进程 ID、机器码或硬件信息。当前没有自动采集或会话录制，也不会调用 identify 将安装 ID 与账户关联。

统计通过 HTTPS 后台发送，失败或离线时不重试、不落盘补报，不阻塞启动或退出。配置读取失败时不会发送启动器打开事件；匿名 ID 无法读取时跳过对应事件。因此，事件数量不一定覆盖所有实际启动。

## 匿名安装 ID

ID 是随机 UUID，保存在启动器目录下的 `ELCL/installation-id`，以后启动读取同一份文件。该文件与 `config.json`、账户数据分开，不包含在配置导出中，也不会被内置缓存清理删除。

删除或重建该文件会产生新的安装 ID。复制整个数据目录可能复制同一个 ID。因此，这项统计衡量的是独立安装，而不是严格意义上的人数或物理电脑数。

在 PostHog 项目 `721980` 的 Trends 中选择事件 `launcher_opened`、聚合方式 **Unique users**、时间粒度 **Day**，即可按安装 ID 查看每日去重数量。查看启动次数时使用 **Total count**。客户端不做每日去重，每次启动都会尝试上报。

游戏启动次数选择事件 `game_launch_succeeded` 和 **Total count**；每天启动过游戏的独立安装数选择该事件和 **Unique users / Day**。窗口重新显示、窗口标题刷新或游戏退出不会重复触发成功事件。

事件名称、具名触发方法及发送协议集中在 `Analytics/AnalyticsEvents.cs`，业务代码通过 `App.Analytics` 触发。新增事件或更换统计服务时，在此文件维护；服务地址与公开 token 仍集中在 `Settings/ServiceConfiguration.cs`。

## 网络与 IP 地址

事件正文不主动采集 IP，并发送 `$ip: null` 与 `$geoip_disable: true`。网络连接本身仍会让接收服务看到出口 IP，不能将这理解为完全匿名的网络连接。

项目维护者还应在 PostHog 的项目设置中关闭 IP 保存；如需项目级禁止地理位置补全，同时关闭 GeoIP transformation。参见 [PostHog 数据收集控制](https://posthog.com/docs/privacy/data-collection)和[匿名事件接口](https://posthog.com/docs/api/capture)。

## 服务配置与机器码

Microsoft 登录 Client ID 和 PostHog Project token 是公开客户端配置，随 ELCL 内置，不再生成或读取 `ELCL/services.json`。升级时已有文件会保留，但不再生效。开发者仍可通过 `ELCL_MICROSOFT_CLIENT_ID` 环境变量覆盖 Microsoft Client ID。

本地机器码方法与统计分开：它基于 Windows 安装标识生成应用专用的 SHA-256 值，不上传原始标识，也不作为 PostHog 的用户 ID。同一 Windows 安装通常保持一致，重装系统或克隆系统可能影响其稳定性或唯一性。
