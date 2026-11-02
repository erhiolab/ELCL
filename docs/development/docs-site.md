# 文档站开发

本站使用 VitePress, 以仓库中的 `docs/` 为唯一内容源. 核心库和启动器文档无需复制到其他目录. 项目使用 pnpm 管理前端依赖, 版本锁定在根目录的 `pnpm-lock.yaml`.

## 本地运行

在仓库根目录安装 Node.js 20 或更新版本及 pnpm, 然后运行:

```powershell
pnpm install
pnpm dev
```

打开 `http://127.0.0.1:3000/`. 本地服务会在 Markdown 或样式变更后更新页面.

## 构建与预览

```powershell
pnpm build
pnpm preview
```

静态站点输出到 `artifacts/docs-site/`, 由 `.gitignore` 排除. `pnpm preview` 用本地服务器检查构建结果. 如果站点部署在子路径, 构建前设置 `DOCS_BASE`, 例如 `/ELCL/`; 默认根路径为 `/`. 当前仓库没有自动部署文档站的流程.

站点导航在 `docs/.vitepress/config.mts`, 样式在 `docs/.vitepress/theme/custom.css`. 新增公开核心 API 页面时, 将页面放到 `docs/core/` 并在配置的 Core 侧栏中加入入口; ELCL 专属流程放到 `docs/launcher/`.
