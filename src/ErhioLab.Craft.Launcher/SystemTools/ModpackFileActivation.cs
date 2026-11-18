using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ErhioLab.Craft.Launcher.SystemTools;

/// <summary>在同一用户\会话和启动器目录的运行实例间传递 MRPACK 路径.</summary>
internal sealed class ModpackFileActivation : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly SecurityIdentifier _user;
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<string> _receive;
    private readonly Action<Exception> _error;
    private readonly Task? _listener;
    internal bool IsPrimary { get; }
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal ModpackFileActivation(string launcherDirectory, Action<string> receive, Action<Exception> error)
    {
        using var identity = WindowsIdentity.GetCurrent();
        _user = identity.User ?? throw new UnauthorizedAccessException("无法确定当前 Windows 用户.");
        string scope = Path.GetFullPath(launcherDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant() + "|" + _user.Value + "|" + Process.GetCurrentProcess().SessionId;
        string hash = Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(scope)));
        _pipeName = "ELCL-Modpack-" + hash;
        _receive = receive;
        _error = error;
        var security = new MutexSecurity();
        security.SetOwner(_user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new MutexAccessRule(_user, MutexRights.FullControl, AccessControlType.Allow));
        _mutex = MutexAcl.Create(false, "Local\\" + _pipeName, out _, security);
        try
        {
            IsPrimary = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }

        if (IsPrimary) _listener = Task.Run(ListenAsync);
    }

    internal static string? ParseArguments(string[] args)
    {
        if (args.Length == 2 && args[0] == "--import-modpack") return ValidatePath(args[1]);
        if (args.Length == 1 && Path.GetExtension(args[0]).Equals(".mrpack", StringComparison.OrdinalIgnoreCase)) return ValidatePath(args[0]);
        if (args.Contains("--import-modpack", StringComparer.Ordinal)) throw new ArgumentException("整合包打开参数无效.");
        return null;
    }

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32768 || path.Any(character => character < 32)
            || !Path.GetExtension(path).Equals(".mrpack", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("需要有效的 MRPACK 文件路径.");
        return Path.GetFullPath(path);
    }

    internal async Task<bool> TryForwardAsync(string path, bool allowForeground = true)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            // CurrentUserOnly 比较的是令牌 Owner, 提权后可能是 Administrators 而非用户 SID.
            // 显式检查账户 SID, 让同一用户的普通权限和管理员窗口可以互相转交文件.
            if (!Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)), _user))
                throw new UnauthorizedAccessException("整合包接收实例不属于当前 Windows 用户.");
            if (allowForeground && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid)) AllowSetForegroundWindow(pid);
            byte[] bytes = Utf8.GetBytes(ValidatePath(path));
            byte[] length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            await pipe.WriteAsync(length, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            byte[] response = new byte[1];
            await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            return response[0] == 1;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException)
        {
            _error(error);
            return false;
        }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreateServer();
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                byte[] header = new byte[4];
                await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is <= 0 or > 131072) throw new InvalidDataException("整合包打开请求过大.");
                byte[] bytes = new byte[length];
                await pipe.ReadExactlyAsync(bytes, timeout.Token).ConfigureAwait(false);
                string raw = Utf8.GetString(bytes);
                if (!Path.IsPathFullyQualified(raw)) throw new InvalidDataException("整合包路径必须是绝对路径.");
                string path = ValidatePath(raw);
                _receive(path);
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
            {
                if (!_stop.IsCancellationRequested) _error(error);
                if (error is UnauthorizedAccessException) return;
                if (!_stop.IsCancellationRequested) await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.SetOwner(_user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(_user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // 监听任务只做异步管道 I/O, 退出时观察异常, 不阻塞 WPF 调度线程.
        if (_listener is not null)
            _ = _listener.ContinueWith(task =>
            {
                if (task.Exception is { } error) _error(error);
                _stop.Dispose();
            }, TaskScheduler.Default);
        else _stop.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
