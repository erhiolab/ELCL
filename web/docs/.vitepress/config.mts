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
            { text: "启动器文档", link: "/launcher/advanced-options", activeMatch: "/launcher/" },
            { text: "核心库文档", link: "/core/README", activeMatch: "/core/" },
            { text: "GitHub", link: "https://github.com/erhiolab/ELCL" }
        ],
        sidebar: {
            "/launcher/": [
                {
                    text: "ELCL 启动器", items: [
                        { text: "高级启动选项", link: "/launcher/advanced-options" }
                    ]
                }
            ],
            "/core/": [
                {
                    text: "入门", items: [
                        { text: "核心库概览", link: "/core/README" },
                        { text: "快速开始", link: "/core/quick-start" },
                        { text: "Core 负责什么", link: "/core/architecture" }
                    ]
                },
                {
                    text: "Minecraft", items: [
                        { text: "游戏版本列表", link: "/core/versions" },
                        { text: "模组加载器版本", link: "/core/loaders" },
                        { text: "Minecraft Wiki", link: "/core/wiki" },
                        { text: "启动模板替换标记", link: "/core/launch-placeholders" },
                        { text: "离线玩家 UUID", link: "/core/offline-accounts" },
                        { text: "Microsoft 账户登录", link: "/core/microsoft-auth" }
                    ]
                },
                {
                    text: "基础能力", items: [
                        { text: "Java 运行时发现", link: "/core/java" },
                        { text: "文件下载", link: "/core/downloader" },
                        { text: "写日志", link: "/core/logging" },
                        { text: "保存 JSON", link: "/core/storage" },
                        { text: "版本号", link: "/core/version" }
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
