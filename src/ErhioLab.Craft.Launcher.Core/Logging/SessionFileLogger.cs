using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ErhioLab.Craft.Launcher.Core.Logging;

/// <summary>将一次程序运行的日志写入独立文件, 并保留最近五次运行记录.</summary>
public sealed class SessionFileLogger : IDisposable
{
    private const int MaxSessionFiles = 5;
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    /// <summary>本次运行写入的日志文件绝对路径.</summary>
    public string FilePath { get; }

    /// <summary>在指定目录开始一次新的日志会话.</summary>
    /// <param name="directoryPath">由调用方决定的日志目录, 可以是相对路径或绝对路径.</param>
    public SessionFileLogger(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        string fullDirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(fullDirectoryPath);
        FilePath = Path.Combine(fullDirectoryPath, "Log1.txt");

        // 轮换和新文件创建必须一起加锁, 避免两个启动器同时写同一个 Log1.txt.
        string directoryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullDirectoryPath)));
        using Mutex rotationMutex = new(false, $"ELCL-Log-{directoryHash}");
        bool lockTaken = false;
        FileStream stream;
        try
        {
            try
            {
                lockTaken = rotationMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken)
            {
                throw new IOException("等待日志轮换锁超时.");
            }

            RotateSessionFiles(fullDirectoryPath);
            stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
        }
        finally
        {
            if (lockTaken)
            {
                rotationMutex.ReleaseMutex();
            }
        }

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
        Information("Logger", "日志会话开始.");
    }

    /// <summary>记录调试信息.</summary>
    public void Debug(string source, string message) => Write(LogLevel.Debug, source, message);

    /// <summary>记录普通操作或状态.</summary>
    public void Information(string source, string message) => Write(LogLevel.Information, source, message);

    /// <summary>记录可继续运行的问题.</summary>
    public void Warning(string source, string message) => Write(LogLevel.Warning, source, message);

    /// <summary>记录失败和可选的异常信息.</summary>
    public void Error(string source, string message, Exception? exception = null) => Write(LogLevel.Error, source, message, exception);

    /// <summary>以统一格式写入一条日志.</summary>
    /// <param name="level">日志级别.</param>
    /// <param name="source">产生此记录的模块或操作.</param>
    /// <param name="message">需要记录的内容.</param>
    /// <param name="exception">需要附加的异常, 可为空.</param>
    public void Write(LogLevel level, string source, string message, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(message);

        string details = exception is null ? string.Empty : $" | {EscapeLine(exception.ToString())}";
        string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        string line = $"{timestamp} [{level}] [{EscapeLine(source)}] {EscapeLine(message)}{details}";

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.WriteLine(line);
        }
    }

    /// <summary>刷新并关闭本次日志文件.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer.Dispose();
            _disposed = true;
        }
    }

    private static void RotateSessionFiles(string directoryPath)
    {
        string oldestFile = Path.Combine(directoryPath, $"Log{MaxSessionFiles}.txt");
        if (File.Exists(oldestFile))
        {
            File.Delete(oldestFile);
        }

        for (int index = MaxSessionFiles - 1; index >= 1; index--)
        {
            string source = Path.Combine(directoryPath, $"Log{index}.txt");
            if (!File.Exists(source))
            {
                continue;
            }

            string destination = Path.Combine(directoryPath, $"Log{index + 1}.txt");
            File.Move(source, destination, overwrite: true);
        }
    }

    private static string EscapeLine(string value) => value
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
