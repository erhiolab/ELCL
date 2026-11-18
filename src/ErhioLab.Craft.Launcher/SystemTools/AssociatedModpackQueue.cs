namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>在界面线程依次处理关联打开的文件, 避免多个安装确认相互覆盖.</summary>
internal sealed class AssociatedModpackQueue(Func<string, Task> import, Func<bool> canProcess, Action activate, Action<string, Exception> onError)
{
    private readonly Queue<string> _pending = new();
    private bool _processing;
    private string? _active;

    internal bool Enqueue(string path)
    {
        if (string.Equals(_active, path, StringComparison.OrdinalIgnoreCase) || _pending.Contains(path, StringComparer.OrdinalIgnoreCase)) return false;
        _pending.Enqueue(path);
        activate();
        _ = ProcessAsync();
        return true;
    }

    internal async Task ProcessAsync()
    {
        if (_processing || !canProcess()) return;
        _processing = true;
        try
        {
            while (_pending.Count > 0 && canProcess())
            {
                _active = _pending.Dequeue();
                try
                {
                    await import(_active);
                }
                catch (Exception error)
                {
                    onError(_active, error);
                }
                finally
                {
                    _active = null;
                }
            }
        }
        finally
        {
            _processing = false;
        }
    }
}
