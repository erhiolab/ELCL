import { defineConfig } from "vitepress"

const base = process.env.DOCS_BASE || "/"

export default defineConfig({
    title: "ELCL · Minecraft Java 启动器",
    description: "ELCL 启动器官网与 ELCLC 核心库文档",
    lang: "zh-CN",
    base,
    cleanUrls: true,
    lastUpdated: true,
    outDir: "../artifacts/docs-site",
    head: [
        ["link", { rel: "icon", type: "image/x-icon", href: `${base}favicon.ico` }],
        ["link", { rel: "apple-touch-icon", href: `${base}logo.png` }],
        ["meta", { property: "og:image", content: `${base}logo.png` }],
        ["meta", { name: "theme-color", content: "#0b1521" }],
        ["meta", { name: "keywords", content: "ELCL, ELCLC, Minecraft, 启动器, 核心库" }]
    ],
    themeConfig: {
        siteTitle: "ELCL",
        logo: "/logo.svg",
        nav: [
            { text: "首页", link: "/" },
            { text: "启动器文档", link: "/launcher/", activeMatch: "/launcher/" },
            { text: "核心库文档", link: "/core/README", activeMatch: "/core/" },
            { text: "GitHub", link: "https://github.com/erhiolab/ELCL" }
        ],
        sidebar: {
            "/launcher/": [
                {
                    text: "开始使用", items: [
                        { text: "功能与使用指南", link: "/launcher/" },
                        { text: "账户管理", link: "/launcher/accounts" },
                        { text: "皮肤与披风", link: "/launcher/appearance" },
                        { text: "版本选择", link: "/launcher/versions" },
                        { text: "下载与安装", link: "/launcher/download" },
                        { text: "启动游戏", link: "/launcher/launch" }
                    ]
                },
                {
                    text: "管理与设置", items: [
                        { text: "版本设置", link: "/launcher/version-settings" },
                        { text: "任务管理", link: "/launcher/tasks" },
                        { text: "启动设置", link: "/launcher/launch-settings" },
                        { text: "高级启动选项", link: "/launcher/advanced-options" },
                        { text: "外观与配置", link: "/launcher/settings" }
                    ]
                },
                {
                    text: "工具与帮助", items: [
                        { text: "百宝箱", link: "/launcher/toolbox" },
                        { text: "性能", link: "/launcher/performance" },
                        { text: "隐私与匿名统计", link: "/launcher/privacy" },
                        { text: "文件与故障排查", link: "/launcher/files" }
                    ]
                }
            ],
            "/core/": [
                {
                    text: "入门", items: [
                        { text: "核心库概览", link: "/core/README" },
                        { text: "快速开始", link: "/core/quick-start" },
                        { text: "接入约定", link: "/core/architecture" },
                        { text: "公开 API 索引", link: "/core/api" }
                    ]
                },
                {
                    text: "版本与安装", items: [
                        { text: "游戏版本列表", link: "/core/versions" },
                        { text: "本地版本扫描", link: "/core/local-versions" },
                        { text: "版本目录管理", link: "/core/version-directories" },
                        { text: "模组加载器版本", link: "/core/loaders" },
                        { text: "安装原版", link: "/core/vanilla-installation" },
                        { text: "安装模组加载器", link: "/core/loader-installation" }
                    ]
                },
                {
                    text: "账户与启动", items: [
                        { text: "Microsoft 登录", link: "/core/microsoft-auth" },
                        { text: "离线玩家 UUID", link: "/core/offline-accounts" },
                        { text: "皮肤与披风", link: "/core/skins" },
                        { text: "准备启动 Minecraft", link: "/core/launch" },
                        { text: "启动模板替换标记", link: "/core/launch-placeholders" },
                        { text: "启动器安装档案", link: "/core/launcher-profiles" }
                    ]
                },
                {
                    text: "通用工具", items: [
                        { text: "Java 发现", link: "/core/java" },
                        { text: "下载器", link: "/core/downloader" },
                        { text: "Minecraft Wiki", link: "/core/wiki" },
                        { text: "日志", link: "/core/logging" },
                        { text: "JSON 存储", link: "/core/storage" },
                        { text: "核心版本号", link: "/core/version" }
                    ]
                }
            ]
        },
        socialLinks: [{ icon: "github", link: "https://github.com/erhiolab/ELCL" }],
        editLink: {
            pattern: "https://github.com/erhiolab/ELCL/edit/main/web/docs/:path",
            text: "在 GitHub 上编辑此页"
        },
        search: {
            provider: "local",
            options: {
                translations: {
                    button: { buttonText: "搜索文档", buttonAriaLabel: "搜索文档" },
                    modal: {
                        noResultsText: "没有找到结果",
                        resetButtonTitle: "清除搜索",
                        footer: { selectText: "选择", navigateText: "切换", closeText: "关闭" }
                    }
                }
            }
        },
        outline: { label: "本页目录", level: [2, 3] },
        docFooter: { prev: "上一页", next: "下一页" },
        lastUpdated: { text: "最后更新于" },
        returnToTopLabel: "返回顶部",
        sidebarMenuLabel: "目录",
        darkModeSwitchLabel: "切换外观"
    }
})
