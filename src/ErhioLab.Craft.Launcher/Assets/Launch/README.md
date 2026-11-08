# 启动兼容补丁

补丁随启动器构建嵌入, 运行时释放到 `ELCL/cache/launch-patches/<SHA-256>/`. 现有文件会校验 SHA-256.

| 文件                   | 来源                                                                                                                                                                                                          | SHA-256                                                            |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `JavaWrapper.jar`      | [PCL 固定提交中的 Java Launch Wrapper 1.4.4](https://github.com/Meloong-Git/PCL/blob/93ede5d9e638c99fcec853a9ba1eecd91ceee7f5/PCLCS/Launch/JavaWrapper.jar), 保留原始二进制; 来源许可见 `JavaWrapper.LICENSE` | `88EB3EE58854E916437831697EF6A2FD493DA9BC4FFB018F620CD88F090952AA` |
| `LwjglUnsafeAgent.jar` | [HMCL-dev/lwjgl-unsafe-agent](https://github.com/HMCL-dev/lwjgl-unsafe-agent), Maven Central `org.glavo:lwjgl-unsafe-agent:2.0`; Apache-2.0, 见 `LwjglUnsafeAgent.LICENSE`                                    | `6C07D3508DC090A12076876BDC227A19CE195A41C9E2C6535C3F82213B1B2015` |

JavaWrapper 用于 Windows Java 命令行编码兼容; 用户禁用或配置其他 `-javaagent` 时不启用. LWJGL Unsafe Agent 只在依赖 `org.lwjgl:lwjgl:3.4.1` 时启用. 两者都可在高级启动选项中关闭.
