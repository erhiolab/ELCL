using System.IO;
using System.Windows.Media.Imaging;

using Windows.Media.Editing;
using Windows.Storage;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal static class VideoThumbnailReader
{
    internal static async Task<BitmapSource> ReadFirstFrameAsync(string path, int maxSize, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(timeout.Token).ConfigureAwait(false);
            var clip = await MediaClip.CreateFromFileAsync(file).AsTask(timeout.Token).ConfigureAwait(false);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            try
            {
                var video = clip.GetVideoEncodingProperties();
                if (video.Width == 0 || video.Height == 0) throw new NotSupportedException("文件没有可用的视频画面.");
                var landscape = video.Width >= video.Height;
                using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.Zero, landscape ? maxSize : 0,
                    landscape ? 0 : maxSize, VideoFramePrecision.NearestFrame).AsTask(timeout.Token).ConfigureAwait(false);
                using var stream = thumbnail.AsStreamForRead();
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            finally
            {
                composition.Clips.Clear();
            }
        }
        catch (OperationCanceledException error) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new IOException("读取视频首帧超时.", error);
        }
    }
}
