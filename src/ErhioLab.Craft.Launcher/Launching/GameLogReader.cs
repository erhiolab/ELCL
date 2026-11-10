using System.IO;
using System.Text;

namespace ErhioLab.Craft.Launcher.Launching;

/// <summary>按实例日志路径增量读取, 限制单次读取量与长行, 不占用写入句柄.</summary>
internal sealed class GameLogReader(string path, bool startAtEnd = false)
{
    internal const int MaxTailBytes = 1024 * 1024;
    private const int MaxLineLength = 16384;
    private readonly byte[] _bytes = new byte[65536];
    private readonly char[] _chars = new char[Encoding.UTF8.GetMaxCharCount(65536)];
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _pending = new();
    private readonly Queue<string> _ready = new();
    private long _offset = startAtEnd ? new FileInfo(path).Length : 0;
    private bool _skipPartial;
    internal bool ReadFinalLine { get; set; }

    internal async Task<(IReadOnlyList<string> Lines, bool Truncated)> ReadAsync(CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        bool truncated = file.Length < _offset || file.Length - _offset > MaxTailBytes;
        if (truncated)
        {
            _offset = Math.Max(0, file.Length - MaxTailBytes);
            _skipPartial = _offset > 0;
            _decoder.Reset();
            _pending.Clear();
            _ready.Clear();
        }

        if (_ready.Count == 0)
        {
            file.Position = _offset;
            int count = await file.ReadAsync(_bytes, token).ConfigureAwait(false);
            _offset += count;
            int chars = _decoder.GetChars(_bytes, 0, count, _chars, 0, flush: false);
            for (int index = 0; index < chars; index++)
            {
                char value = _chars[index];
                if (_skipPartial)
                {
                    if (value == '\n') _skipPartial = false;
                    continue;
                }

                if (value == '\n')
                {
                    _ready.Enqueue(_pending.ToString().TrimEnd('\r'));
                    _pending.Clear();
                }
                else
                {
                    _pending.Append(value);
                    if (_pending.Length >= MaxLineLength)
                    {
                        _ready.Enqueue(_pending.ToString());
                        _pending.Clear();
                    }
                }
            }

            if (count == 0 && ReadFinalLine && _pending.Length > 0)
            {
                _ready.Enqueue(_pending.ToString().TrimEnd('\r'));
                _pending.Clear();
            }
        }

        List<string> lines = [];
        while (lines.Count < 512 && _ready.TryDequeue(out var line)) lines.Add(line);
        return (lines, truncated);
    }
}
