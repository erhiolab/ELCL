using ErhioLab.Craft.Launcher.Core.Mods;

namespace ErhioLab.Craft.Launcher.Mods;

/// <summary>当前搜索会话的有限页缓存, 前台翻页与后台预取复用同一个请求.</summary>
internal sealed class ModSearchPageCache(Func<ModSearchRequest, CancellationToken, Task<ModSearchResult>> fetch) : IDisposable
{
    private readonly Lock _sync = new();
    private readonly Dictionary<ModSearchRequest, Entry> _pages = [];
    private long _sequence;
    private bool _disposed;

    internal Task<ModSearchResult> GetAsync(ModSearchRequest request)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pages.TryGetValue(request, out var previous))
            {
                if (!previous.Task.IsCanceled && !previous.Task.IsFaulted && (!previous.Task.IsCompletedSuccessfully || previous.Task.Result.FailedPlatforms.Count == 0))
                {
                    previous.LastUsed = ++_sequence;
                    return previous.Task;
                }

                _pages.Remove(request);
            }

            var entry = new Entry { LastUsed = ++_sequence };
            var token = entry.Cancellation.Token;
            entry.Task = Task.Run(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var result = await fetch(request, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return result;
                }
                finally
                {
                    lock (_sync)
                    {
                        entry.Finished = true;
                        entry.Cancellation.Dispose();
                    }
                }
            }, CancellationToken.None);
            // 离页后无人等待的失败任务也必须观察异常.
            _ = entry.Task.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            _pages.Add(request, entry);
            while (_pages.Count > 3)
            {
                var oldest = _pages.MinBy(pair => pair.Value.LastUsed);
                _pages.Remove(oldest.Key);
                if (!oldest.Value.Finished) oldest.Value.Cancellation.Cancel();
            }

            return entry.Task;
        }
    }

    internal void CancelPending()
    {
        lock (_sync)
        {
            foreach (var pair in _pages.Where(pair => !pair.Value.Finished).ToArray())
            {
                _pages.Remove(pair.Key);
                pair.Value.Cancellation.Cancel();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPending();
            _pages.Clear();
        }
    }

    private sealed class Entry
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal Task<ModSearchResult> Task = null!;
        internal long LastUsed;
        internal bool Finished;
    }
}
