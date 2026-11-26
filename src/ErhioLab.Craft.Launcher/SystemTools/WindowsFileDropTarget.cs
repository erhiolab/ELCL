using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>接收拖入窗口的文件, 并交给当前可接收文件的页面.</summary>
internal sealed class WindowsFileDropTarget : IDisposable
{
    private const int WmDropFiles = 0x0233;
    private const int WmCopyGlobalData = 0x0049;
    private const int FileCountLimit = 4096;
    private static readonly ConditionalWeakTable<HwndSource, Registration> Registrations = new();

    private readonly FrameworkElement _target;
    private readonly Func<string[], bool> _acceptsFiles;
    private readonly Action<string[]> _filesDropped;
    private readonly Action<Exception> _failed;
    private readonly Func<Window, bool> _isActiveRoute;
    private readonly bool _useNative;
    private Registration? _registration;
    private bool _enabled = true;
    private bool _disposed;

    internal static WindowsFileDropTarget Attach(FrameworkElement target, Func<string[], bool> acceptsFiles,
        Action<string[]> filesDropped, Action<Exception> failed, Func<Window, bool> isActiveRoute) =>
        new(target, acceptsFiles, filesDropped, failed, isActiveRoute, WindowsProcessPrivileges.IsElevated);

    private WindowsFileDropTarget(FrameworkElement target, Func<string[], bool> acceptsFiles, Action<string[]> filesDropped,
        Action<Exception> failed, Func<Window, bool> isActiveRoute, bool useNative)
    {
        _target = target;
        _acceptsFiles = acceptsFiles;
        _filesDropped = filesDropped;
        _failed = failed;
        _isActiveRoute = isActiveRoute;
        _useNative = useNative;
        PresentationSource.AddSourceChangedHandler(target, SourceChanged);
        Connect();
    }

    private void SourceChanged(object sender, SourceChangedEventArgs e) => Connect();

    private void Connect()
    {
        var source = PresentationSource.FromVisual(_target) as HwndSource;
        if (_registration?.Source == source) return;
        _registration?.Remove(this);
        _registration = null;
        if (source is not { IsDisposed: false, RootVisual: Window window } || source.Handle == IntPtr.Zero) return;

        var registration = Registrations.GetValue(source, key => new Registration(key, window, _useNative));
        _registration = registration;
        registration.Add(this);
    }

    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _registration?.UpdateAcceptance();
    }

    private bool CanReceive(Window window) => !_disposed && _enabled && _target.IsLoaded && _target.IsVisible
        && _target.IsHitTestVisible && _target.IsEnabled && _isActiveRoute(window);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PresentationSource.RemoveSourceChangedHandler(_target, SourceChanged);
        _registration?.Remove(this);
        _registration = null;
    }

    private sealed class Registration : IDisposable
    {
        internal HwndSource Source { get; }
        private readonly Window _window;
        private readonly IntPtr _handle;
        private readonly List<WindowsFileDropTarget> _targets = [];
        private IntPtr _oleTarget;
        private bool _oleSuspended;
        private bool _nativeReady;
        private bool _disposed;

        internal Registration(HwndSource source, Window window, bool useNative)
        {
            Source = source;
            _window = window;
            _handle = source.Handle;
            Source.Disposed += SourceDisposed;
            _window.PreviewDragEnter += DragOver;
            _window.PreviewDragOver += DragOver;
            _window.PreviewDrop += Drop;
            if (useNative) EnableNativeDrop();
        }

        private void EnableNativeDrop()
        {
            try
            {
                if (!ChangeWindowMessageFilterEx(_handle, WmDropFiles, 1, IntPtr.Zero)
                    || !ChangeWindowMessageFilterEx(_handle, WmCopyGlobalData, 1, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                _oleTarget = GetProp(_handle, "OleDropTargetInterface");
                if (_oleTarget != IntPtr.Zero)
                {
                    Marshal.AddRef(_oleTarget);
                    Marshal.ThrowExceptionForHR(RevokeDragDrop(_handle));
                    _oleSuspended = true;
                }
                Source.AddHook(MessageHook);
                _nativeReady = true;
                App.Logger.Information("DragDrop", $"已启用管理员窗口的 Windows 文件拖放接收, 原 OLE 目标已暂停={_oleSuspended}.");
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or COMException)
            {
                ReleaseNativeDrop();
                App.Logger.Error("DragDrop", "无法初始化 Windows 文件拖放接收, 保留普通拖放.", error);
            }
        }

        internal void Add(WindowsFileDropTarget target)
        {
            _targets.Add(target);
            UpdateAcceptance();
        }

        internal void Remove(WindowsFileDropTarget target)
        {
            _targets.Remove(target);
            UpdateAcceptance();
            if (!_disposed && _targets.Count == 0)
                Source.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    if (_targets.Count == 0) Dispose();
                });
        }

        internal void UpdateAcceptance()
        {
            if (_nativeReady && !_disposed) DragAcceptFiles(_handle, _targets.Any(target => target._enabled));
        }

        private WindowsFileDropTarget? FindActiveTarget()
        {
            WindowsFileDropTarget? active = null;
            foreach (var target in _targets)
            {
                if (!target.CanReceive(_window)) continue;
                if (active is not null) return null;
                active = target;
            }
            return active;
        }

        private void DragOver(object sender, DragEventArgs e) => HandleWpfDrop(e, dropping: false);
        private void Drop(object sender, DragEventArgs e) => HandleWpfDrop(e, dropping: true);

        private void HandleWpfDrop(DragEventArgs e, bool dropping)
        {
            if (FindActiveTarget() is not { } receiver) return;
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return;
                e.Handled = true;
                e.Effects = DragDropEffects.None;
                if ((e.AllowedEffects & DragDropEffects.Copy) == 0 || e.Data.GetData(DataFormats.FileDrop, false) is not string[] files
                    || files.Length is < 1 or > FileCountLimit || !receiver._acceptsFiles(files)) return;
                e.Effects = DragDropEffects.Copy;
                if (dropping) QueueDrop(receiver, files, "WPF");
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException)
            {
                e.Handled = true;
                e.Effects = DragDropEffects.None;
                if (dropping) receiver._failed(error);
            }
        }

        private IntPtr MessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != WmDropFiles || handled) return IntPtr.Zero;
            handled = true;
            WindowsFileDropTarget? receiver = null;
            try
            {
                // 资源管理器已经选定接收窗口. 这里按当前页面分发, 不用 HDROP 坐标再判断一次.
                receiver = FindActiveTarget();
                if (receiver is null)
                {
                    App.Logger.Information("DragDrop", "忽略 Windows 文件拖放: 没有可接收文件的活动页面.");
                    return IntPtr.Zero;
                }

                uint count = DragQueryFile(wParam, uint.MaxValue, null, 0);
                if (count is 0 or > FileCountLimit) throw new InvalidDataException("拖入的文件数量无效或超过 4096 个.");
                string[] files = new string[count];
                for (uint index = 0; index < count; index++)
                {
                    uint length = DragQueryFile(wParam, index, null, 0);
                    if (length is 0 or > 32767) throw new InvalidDataException("拖入的文件路径无效或过长.");
                    var path = new StringBuilder((int)length + 1);
                    if (DragQueryFile(wParam, index, path, (uint)path.Capacity) != length) throw new InvalidDataException("无法读取拖入的文件路径.");
                    files[index] = path.ToString();
                }

                QueueDrop(receiver, files, "Windows");
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or Win32Exception)
            {
                App.Logger.Error("DragDrop", "读取 Windows 文件拖放失败.", error);
                if (receiver is { } target) _window.Dispatcher.BeginInvoke(() =>
                {
                    if (ReferenceEquals(FindActiveTarget(), target)) target._failed(error);
                });
            }
            finally
            {
                DragFinish(wParam);
            }

            return IntPtr.Zero;
        }

        private void QueueDrop(WindowsFileDropTarget receiver, string[] files, string transport)
        {
            _window.Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(FindActiveTarget(), receiver)) return;
                App.Logger.Information("DragDrop", $"收到 {transport} 文件拖放: 目标={receiver._target.GetType().Name}, 文件数={files.Length}.");
                receiver._filesDropped(files);
            });
        }

        private void SourceDisposed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Registrations.Remove(Source);
            Source.Disposed -= SourceDisposed;
            _window.PreviewDragEnter -= DragOver;
            _window.PreviewDragOver -= DragOver;
            _window.PreviewDrop -= Drop;
            if (_nativeReady) ReleaseNativeDrop();
            _targets.Clear();
        }

        private void ReleaseNativeDrop()
        {
            if (_nativeReady && !Source.IsDisposed) Source.RemoveHook(MessageHook);
            _nativeReady = false;
            if (IsWindow(_handle))
            {
                DragAcceptFiles(_handle, false);
                _ = ChangeWindowMessageFilterEx(_handle, WmDropFiles, 0, IntPtr.Zero);
                _ = ChangeWindowMessageFilterEx(_handle, WmCopyGlobalData, 0, IntPtr.Zero);
                if (_oleSuspended && !Source.IsDisposed && GetProp(_handle, "OleDropTargetInterface") == IntPtr.Zero)
                {
                    int result = RegisterDragDrop(_handle, _oleTarget);
                    if (result < 0) App.Logger.Error("DragDrop", "恢复窗口 OLE 拖放注册失败.", Marshal.GetExceptionForHR(result));
                }
            }
            if (_oleTarget != IntPtr.Zero) Marshal.Release(_oleTarget);
            _oleTarget = IntPtr.Zero;
            _oleSuspended = false;
        }
    }

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? path, uint length);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr drop);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, int message, uint action, IntPtr changeFilter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPropW")]
    private static extern IntPtr GetProp(IntPtr hwnd, string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, IntPtr dropTarget);
}
