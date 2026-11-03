import { defineConfig } from "vitepress"

const base = process.env.DOCS_BASE || "/"

export default defineConfig({
    title: "ELCL Docs",
    description: "ErhioLab Craft Launcher 与 ELCLC 核心库文档",
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
        siteTitle: "ELCL / DOCS",
        logo: "/logo.svg",
        nav: [
            { text: "核心库", link: "/core/README", activeMatch: "/core/" },
            { text: "启动器", link: "/launcher/README", activeMatch: "/launcher/" },
            { text: "开发", link: "/development/code-style", activeMatch: "/development/|/ui/|/publishing" },
            { text: "GitHub", link: "https://github.com/erhiolab/ELCL" }
        ],
        sidebar: {
            "/core/": [
                {
                    text: "入门", items: [
                        { text: "核心库概览", link: "/core/README" },
                        { text: "快速开始", link: "/core/quick-start" },
                        { text: "设计边界", link: "/core/architecture" }
                    ]
                },
                {
                    text: "Minecraft", items: [
                        { text: "原版版本清单", link: "/core/versions" },
                        { text: "附加加载器", link: "/core/loaders" },
                        { text: "Wiki 页面", link: "/core/wiki" }
                    ]
                },
                {
                    text: "基础能力", items: [
                        { text: "通用下载器", link: "/core/downloader" },
                        { text: "会话日志", link: "/core/logging" },
                        { text: "JSON 文件存储", link: "/core/storage" },
                        { text: "核心版本", link: "/core/version" }
                    ]
                }
            ],
            "/launcher/": [
                {
                    text: "ELCL 启动器", items: [
                        { text: "集成说明", link: "/launcher/README" },
                        { text: "配置系统", link: "/launcher/settings" },
                        { text: "下载页面", link: "/launcher/download" },
                        { text: "百宝箱", link: "/launcher/toolbox" }
                    ]
                }
            ],
            "/ui/": [
                {
                    text: "界面设计", items: [
                        { text: "视觉方向", link: "/ui/visual-direction" },
                        { text: "主题", link: "/ui/themes" },
                        { text: "通知", link: "/ui/notifications" },
                        { text: "对话框", link: "/ui/dialogs" }
                    ]
                }
            ],
            "/development/": [
                {
                    text: "开发", items: [
                        { text: "代码规范", link: "/development/code-style" },
                        { text: "文档站开发", link: "/development/docs-site" },
                        { text: "构建发布", link: "/publishing" }
                    ]
                }
            ]
        },
        socialLinks: [{ icon: "github", link: "https://github.com/erhiolab/ELCL" }],
        editLink: {
            pattern: "https://github.com/erhiolab/ELCL/edit/main/docs/:path",
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
