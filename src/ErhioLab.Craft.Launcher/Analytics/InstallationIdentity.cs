using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ErhioLab.Craft.Launcher.Analytics;

/// <summary>保存随机安装标识, 与配置导入/导出/账户和硬件标识隔离.</summary>
internal static class InstallationIdentity
{
    internal static string GetOrCreate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\ELCL-InstallationId-" + key);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired) throw new IOException("等待匿名安装标识写入超时.");

            if (File.Exists(fullPath))
            {
                var text = File.ReadAllText(fullPath).Trim();
                if (Guid.TryParseExact(text, "D", out var saved) && saved != Guid.Empty) return saved.ToString("D");
                // 无法读取已有标识时跳过统计, 不静默生成一个新用户.
                throw new InvalidDataException("匿名安装标识无效, 已保留原文件.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var id = Guid.NewGuid().ToString("D");
            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(id);
            return id;
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
