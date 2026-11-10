---
layout: home
title: ELCL · Minecraft Java 启动器
description: 安装 Minecraft、管理账户与版本，阅读 ELCL 使用指南和 ELCLC 核心 API 文档。
---

<div class="site-home">
  <section class="home-hero">
    <div class="home-hero-copy">
      <span class="home-eyebrow"><span class="home-live-dot" aria-hidden="true"></span> ERHIOLAB CRAFT LAUNCHER</span>
      <h1>ELCL<br><span>方寸之间，自有天地。</span></h1>
      <p class="home-lead">给旧存档留一盏灯，给新世界留一扇门。<br>从熟悉的出生点，到还没去过的远方。</p>
      <div class="home-actions">
        <a class="home-button home-button-primary" href="./launcher/">开始使用</a>
        <a class="home-button home-button-secondary" href="https://github.com/erhiolab/ELCL">在 GitHub 查看项目</a>
      </div>
      <p class="home-hero-note">Windows · .NET 10 · 开源开发中</p>
    </div>
    <div class="home-logo-stage" aria-label="ELCL 标志">
      <div class="home-logo-orbit" aria-hidden="true"></div>
      <img class="home-logo-image" src="/logo.svg" alt="ELCL" width="310" height="310">
      <span class="home-logo-label">ERHIOLAB / CRAFT LAUNCHER</span>
    </div>
  </section>

  <section class="home-section home-intro" aria-labelledby="launcher-features">
    <div class="home-section-heading">
      <span class="home-kicker">日常使用</span>
      <h2 id="launcher-features">从安装到进入世界。</h2>
      <p>常用功能都有入口，遇到问题也能找到对应的步骤。</p>
    </div>
    <div class="home-feature-grid">
      <a class="home-feature" href="./launcher/download">
        <span class="feature-number">01 / 下载</span>
        <h3>原版与加载器一起安装</h3>
        <p>支持 Forge、NeoForge、Fabric、Quilt、LiteLoader 和 OptiFine。多文件并发下载，首选来源失败后尝试备用来源。</p>
        <span class="home-feature-link">查看安装方法</span>
      </a>
      <a class="home-feature" href="./launcher/accounts">
        <span class="feature-number">02 / 账户</span>
        <h3>选好这次的玩家身份</h3>
        <p>添加 Microsoft 账户或离线用户，双击切换。正版账户还可以预览三维皮肤、上传皮肤和选择已有披风。</p>
        <span class="home-feature-link">打开账户指南</span>
      </a>
      <a class="home-feature" href="./launcher/version-settings">
        <span class="feature-number">03 / 版本</span>
        <h3>每个版本都有自己的位置</h3>
        <p>记住上次选择，填写描述，打开存档或模组文件夹。需要时补全文件，或导出一个启动脚本。</p>
        <span class="home-feature-link">了解版本管理</span>
      </a>
      <a class="home-feature" href="./launcher/launch-settings">
        <span class="feature-number">04 / 启动</span>
        <h3>把运行环境安排好</h3>
        <p>查找并排序 Java，选择窗口大小、进程优先级和内存分配。启动前检查账户与游戏文件。</p>
        <span class="home-feature-link">调整启动设置</span>
      </a>
      <a class="home-feature" href="./launcher/tasks">
        <span class="feature-number">05 / 任务</span>
        <h3>进度和错误放在一起</h3>
        <p>安装、补全和启动都有任务记录。可以取消准备，或终止某个运行实例；多个游戏也能分别管理。</p>
        <span class="home-feature-link">认识任务列表</span>
      </a>
      <a class="home-feature" href="./launcher/toolbox">
        <span class="feature-number">06 / 工具</span>
        <h3>还有一些顺手的小工具</h3>
        <p>下载自定义文件、清理缓存、优化内存，或看看今天的人品。性能页面可以随时查看硬件与系统占用。</p>
        <span class="home-feature-link">逛逛百宝箱</span>
      </a>
    </div>
  </section>

  <section class="home-core" aria-labelledby="core-intro">
    <div>
      <span class="home-kicker">给开发者</span>
      <h2 id="core-intro">你的启动器，也能用 ELCLC。</h2>
      <p>ELCLC 是独立的 .NET 10 核心库。查询版本、发现 Java、下载文件、安装加载器，再生成游戏启动参数。你提供网络客户端与路径，界面和配置由你决定。</p>
      <div class="home-actions">
        <a class="home-button home-button-primary" href="./core/quick-start">运行第一个示例</a>
        <a class="home-button home-button-secondary" href="./core/api">查找 API</a>
      </div>
      <p class="home-hero-note">每个方法单独说明，附调用示例、返回结果与注意事项。</p>
    </div>
    <div class="home-code">
      <div class="code-top"><span></span><span></span><span></span><small>Program.cs</small></div>
      <pre><code>using ErhioLab.Craft.Launcher.Core.Minecraft;&#10;&#10;using var http = new HttpClient();&#10;var client = new MinecraftVersionCatalogClient(http);&#10;var catalog = await client.GetCatalogAsync(&#10;    MinecraftManifestSource.Official&#10;);&#10;&#10;Console.WriteLine(catalog.LatestReleaseId);</code></pre>
    </div>
  </section>

  <section class="home-status" aria-labelledby="docs-start">
    <span class="home-kicker">从这里继续</span>
    <h2 id="docs-start">找你现在需要的那一页。</h2>
    <div class="home-guide-grid">
      <div class="home-guide">
        <h3>我在使用 ELCL</h3>
        <p>从第一次安装，到整理游戏和排查启动问题。</p>
        <a class="home-text-link" href="./launcher/">完整功能与使用指南</a>
        <a class="home-text-link" href="./launcher/files">文件位置与故障排查</a>
        <a class="home-text-link" href="./launcher/privacy">隐私与匿名统计</a>
      </div>
      <div class="home-guide">
        <h3>我在接入 ELCLC</h3>
        <p>先跑通一个请求，再组合安装与启动流程。</p>
        <a class="home-text-link" href="./core/README">核心能力概览</a>
        <a class="home-text-link" href="./core/vanilla-installation">按步骤安装原版</a>
        <a class="home-text-link" href="./core/launch">准备启动一个版本</a>
      </div>
    </div>
    <p class="home-project-note">项目仍在开发。当前功能和限制以文档为准，问题与建议可以提交到 <a href="https://github.com/erhiolab/ELCL/issues">GitHub Issues</a>。</p>
  </section>
  <footer class="home-footer"><span>ELCL <span class="footer-divider">/</span> ErhioLab Craft Launcher</span><span>Minecraft Java 版启动器与通用核心库</span></footer>
</div>
