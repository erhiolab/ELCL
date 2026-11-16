# 图片处理依赖

Mods 图标使用 PhotoSauce.MagicScaler 0.15.0 和 PhotoSauce.NativeCodecs.Libwebp 1.6.0-preview1。普通图片通过 Windows WIC 处理，WebP 使用 libwebp 1.6.0。图片处理属于启动器界面功能，不引入核心库。

- 项目：https://github.com/saucecontrol/PhotoSauce
- `PhotoSauce-LICENSE.txt`：来自上述 NuGet 包的 MIT 许可。
- `PhotoSauce-NOTICES.txt`：来自上述 NuGet 包的第三方声明。
- `libwebp-COPYING.txt`：来自 https://github.com/webmproject/libwebp/blob/v1.6.0/COPYING 的 BSD 许可。

三个声明文件作为嵌入资源随启动器分发，不增加单文件发布旁的必需附属文件。
