---
layout: home
title: ELCL · Minecraft Java 启动器
description: 认识 ELCL 启动器，查阅 ELCLC 核心库文档。
---

<div class="site-home">
  <section class="home-hero">
    <div class="home-hero-copy">
      <span class="home-eyebrow"><span class="home-live-dot"></span> ERHIOLAB CRAFT LAUNCHER</span>
      <h1>ELCL<br><span>从版本开始。</span></h1>
      <p class="home-lead">ELCL 是一款正在开发的 Minecraft Java 版启动器。挑选游戏版本、查找 Java、管理下载，把常用的事放在顺手的位置。</p>
      <div class="home-actions">
        <a class="home-button home-button-primary" href="https://github.com/erhiolab/ELCL">在 GitHub 查看项目 <span aria-hidden="true">↗</span></a>
        <a class="home-button home-button-secondary" href="./core/README">阅读核心库文档 <span aria-hidden="true">→</span></a>
      </div>
      <p class="home-hero-note">开源开发中 · Windows · .NET 10</p>
    </div>
    <div class="home-logo-stage" aria-label="ELCL 标志">
      <div class="home-logo-orbit"></div>
      <img class="home-logo-image" src="/logo.svg" alt="ELCL 标志">
      <span class="home-logo-label">ERHIOLAB / CRAFT LAUNCHER</span>
    </div>
  </section>

  <section class="home-section home-intro">
    <div class="home-section-heading"><span class="home-kicker">THE LAUNCHER</span><h2>打开 ELCL，先做好这些事。</h2><p>从找版本到整理下载任务，日常操作不必绕路。</p></div>
    <div class="home-feature-grid">
      <article class="home-feature"><span class="feature-number">01 / 版本</span><div class="feature-icon">◇</div><h3>找版本更快</h3><p>正式版、预览版、远古版和愚人节版分开显示；搜索版本号，点开就能继续配置。</p></article>
      <article class="home-feature"><span class="feature-number">02 / 环境</span><div class="feature-icon">☕</div><h3>整理本机 Java</h3><p>自动发现已安装的 Java，也能手动添加、排序和移除。需要哪个版本，由你决定优先级。</p></article>
      <article class="home-feature"><span class="feature-number">03 / 下载</span><div class="feature-icon">↓</div><h3>看得见的下载任务</h3><p>多连接下载、来源回退和任务进度已经接入。百宝箱还可以下载自定义文件。</p></article>
    </div>
  </section>

  <section class="home-core"><div><span class="home-kicker">FOR DEVELOPERS</span><h2>ELCLC，给启动器写的通用核心。</h2><p>版本清单、模组加载器版本、Java 发现、文件下载、日志与 JSON 存储。核心库不依赖 ELCL 的窗口或设置，你可以在自己的 .NET 10 项目里使用。</p><a class="home-text-link" href="./core/quick-start">从第一个版本查询开始 <span aria-hidden="true">↗</span></a></div><div class="home-code"><div class="code-top"><span></span><span></span><span></span><small>Program.cs</small></div><pre><code>using var http = new HttpClient();&#10;var client = new MinecraftVersionCatalogClient(http);&#10;var catalog = await client.GetCatalogAsync(&#10;    MinecraftManifestSource.Official);&#10;&#10;Console.WriteLine(catalog.LatestReleaseId);</code></pre></div></section>

  <section class="home-status"><span class="home-kicker">PROJECT STATUS</span><h2>项目还在路上。</h2><p>目前可以浏览游戏与加载器版本、管理 Java 和使用通用下载器。Minecraft 游戏文件安装、账户登录和启动游戏仍在开发中。欢迎在仓库里了解进度或参与讨论。</p><a class="home-text-link" href="https://github.com/erhiolab/ELCL">前往 GitHub <span aria-hidden="true">↗</span></a></section>
  <footer class="home-footer"><span>ELCL <span class="footer-divider">/</span> ErhioLab Craft Launcher</span><span>为 Minecraft Java 版而做 · 开源开发中</span></footer>
</div>
