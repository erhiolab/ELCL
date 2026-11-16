using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Libwebp;

namespace ErhioLab.Craft.Launcher.Mods;

/// <summary>在后台 MTA 线程通过系统编解码器及专用 WebP 编解码器生成可跨线程使用的 Mods 缩略图.</summary>
internal static class ModIconDecoder
{
    private static readonly Lazy<bool> Codecs = new(() =>
    {
        CodecManager.Configure(codecs => codecs.UseLibwebp());
        return true;
    });

    internal static BitmapSource? Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 and <= 4 * 1024 * 1024 }) return null;
        _ = Codecs.Value;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            // 仅读取头信息, 在像素解码前拒绝过大图片. 动画图标只取首帧.
            var info = ImageFileInfo.Load(input);
            if (info.Frames.Count == 0) return null;
            var frame = info.Frames[0];
            if (frame.Width <= 0 || frame.Height <= 0 || (long)frame.Width * frame.Height > 16_777_216) return null;
            input.Position = 0;
            var settings = new ProcessImageSettings
            {
                Width = 64,
                Height = 64,
                ResizeMode = CropScaleMode.Stretch,
                Sharpen = false
            };
            settings.TrySetEncoderFormat(ImageMimeTypes.Png);
            using var output = new MemoryStream();
            MagicImageProcessor.ProcessImage(input, output, settings);
            output.Position = 0;
            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = output;
            result.EndInit();
            result.Freeze();
            return result;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or FormatException or NotSupportedException or COMException)
        {
            return null;
        }
    }
}
