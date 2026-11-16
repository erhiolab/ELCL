using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>在管理员窗口中接收资源管理器的文件拖放, 仅在目标页面可用时注册.</summary>
internal sealed class WindowsFileDropTarget : IDisposable
{
    private const int WmDropFiles = 0x0233;
    private const int WmCopyGlobalData = 0x0049;
    private readonly FrameworkElement _target;
    private readonly Action<string[]> _filesDropped;
    private readonly Action<Exception> _failed;
    private readonly Window _window;
    private readonly HwndSource _source;
    private readonly IntPtr _handle;
    private IntPtr _oleTarget;
    private bool _oleSuspended;
    private bool _disposed;

    internal static WindowsFileDropTarget? TryAttach(FrameworkElement target, Action<string[]> filesDropped, Action<Exception> failed)
    {
        using var identity = WindowsIdentity.GetCurrent();
        bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        App.Logger.Information("DragDrop", $"初始化文件拖入: 管理员权限={elevated}.");
        if (!elevated) return null;
        try
        {
            return new WindowsFileDropTarget(target, filesDropped, failed);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or COMException)
        {
            App.Logger.Error("DragDrop", "无法初始化 Windows 文件拖放接收.", error);
            return null;
        }
    }

    private WindowsFileDropTarget(FrameworkElement target, Action<string[]> filesDropped, Action<Exception> failed)
    {
        _target = target;
        _filesDropped = filesDropped;
        _failed = failed;
        _window = Window.GetWindow(target) ?? throw new InvalidOperationException("文件拖放目标未连接到窗口.");
        _handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_handle) ?? throw new InvalidOperationException("文件拖放窗口句柄不可用.");
        try
        {
            // 仅放行文件拖放与其数据传输消息, 不修改其他窗口或系统策略.
            if (!ChangeWindowMessageFilterEx(_handle, WmDropFiles, 1, IntPtr.Zero)
                || !ChangeWindowMessageFilterEx(_handle, WmCopyGlobalData, 1, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            // Explorer 优先选择已注册的 OLE 目标. 管理员窗口必须先退出此路径,
            // 否则即使接收了 WM_DROPFILES, 跨权限拖入仍会被 OLE 拒绝.
            _oleTarget = GetProp(_handle, "OleDropTargetInterface");
            if (_oleTarget != IntPtr.Zero)
            {
                // 保存同一 COM 目标的引用, 页面退出时恢复原注册.
                Marshal.AddRef(_oleTarget);
                Marshal.ThrowExceptionForHR(RevokeDragDrop(_handle));
                _oleSuspended = true;
            }

            _source.AddHook(MessageHook);
            SetEnabled(true);
            App.Logger.Information("DragDrop", $"已启用管理员窗口的 Windows 文件拖放接收, 原 OLE 目标已暂停={_oleSuspended}.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void SetEnabled(bool enabled)
    {
        if (!_disposed) DragAcceptFiles(_handle, enabled);
    }

    private IntPtr MessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmDropFiles) return IntPtr.Zero;
        handled = true;
        try
        {
            App.Logger.Information("DragDrop", $"收到 Windows 文件拖放消息: 页面已加载={_target.IsLoaded}, 可见={_target.IsVisible}.");
            if (_disposed || !_target.IsLoaded || !_target.IsVisible) return IntPtr.Zero;
            // 返回值表示坐标属于客户区, 不是读取成功与否. 非客户区坐标需要从屏幕转换.
            bool clientArea = DragQueryPoint(wParam, out var point);
            if (!clientArea && !ScreenToClient(_handle, ref point)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var location = new Point(point.X, point.Y);
            if (_source.CompositionTarget is { } composition) location = composition.TransformFromDevice.Transform(location);
            var hit = _window.InputHitTest(location) as DependencyObject;
            if (!HitsTarget(hit))
            {
                App.Logger.Information("DragDrop", $"忽略当前 Mods 页面以外的拖放: 客户区={clientArea}, 坐标={location}, 命中={hit?.GetType().Name ?? "null"}.");
                return IntPtr.Zero;
            }

            uint count = DragQueryFile(wParam, uint.MaxValue, null, 0);
            if (count is 0 or > 4096) throw new InvalidDataException("拖入的文件数量无效或超过 4096 个.");
            string[] files = new string[count];
            for (uint index = 0; index < count; index++)
            {
                uint length = DragQueryFile(wParam, index, null, 0);
                if (length is 0 or > 32767) throw new InvalidDataException("拖入的文件路径无效或过长.");
                var path = new StringBuilder((int)length + 1);
                if (DragQueryFile(wParam, index, path, (uint)path.Capacity) != length) throw new InvalidDataException("无法读取拖入的文件路径.");
                files[index] = path.ToString();
            }

            App.Logger.Information("DragDrop", $"收到 Windows 文件拖放: 文件数={files.Length}, 客户区={clientArea}, 坐标={location}.");
            _target.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && _target.IsLoaded && _target.IsVisible) _filesDropped(files);
            });
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            App.Logger.Error("DragDrop", "读取 Windows 文件拖放失败.", error);
            _target.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && _target.IsLoaded && _target.IsVisible) _failed(error);
            });
        }
        finally
        {
            DragFinish(wParam);
        }

        return IntPtr.Zero;
    }

    private bool HitsTarget(DependencyObject? item)
    {
        while (item is not null)
        {
            if (ReferenceEquals(item, _target)) return true;
            item = item is Visual ? VisualTreeHelper.GetParent(item)
                : item is ContentElement content ? ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent : null;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DragAcceptFiles(_handle, false);
        if (!_source.IsDisposed) _source.RemoveHook(MessageHook);
        _ = ChangeWindowMessageFilterEx(_handle, WmDropFiles, 0, IntPtr.Zero);
        _ = ChangeWindowMessageFilterEx(_handle, WmCopyGlobalData, 0, IntPtr.Zero);
        if (_oleTarget != IntPtr.Zero)
        {
            try
            {
                if (_oleSuspended && IsWindow(_handle))
                {
                    int result = RegisterDragDrop(_handle, _oleTarget);
                    if (result < 0) App.Logger.Error("DragDrop", "恢复窗口 OLE 拖放注册失败.", Marshal.GetExceptionForHR(result));
                }
            }
            finally
            {
                Marshal.Release(_oleTarget);
                _oleTarget = IntPtr.Zero;
                _oleSuspended = false;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? path, uint length);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DragQueryPoint(IntPtr drop, out NativePoint point);

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, IntPtr dropTarget);
}
