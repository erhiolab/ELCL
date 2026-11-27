using System.Net;
using System.Runtime.InteropServices;

using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal static class WindowsMinecraftSrv
{
    private const ushort DnsTypeSrv = 33;

    internal static Task<MinecraftServerAddress?> ResolveAsync(MinecraftServerAddress address, CancellationToken token) =>
        address.HasExplicitPort || IPAddress.TryParse(address.Host, out _)
            ? Task.FromResult<MinecraftServerAddress?>(null)
            : Task.Run(() => Query(address.Host), token).WaitAsync(token);

    private static MinecraftServerAddress? Query(string host)
    {
        nint records = 0;
        try
        {
            if (DnsQuery("_minecraft._tcp." + host, DnsTypeSrv, 0, 0, out records, 0) != 0) return null;
            MinecraftServerAddress? best = null;
            ushort priority = ushort.MaxValue;
            for (nint pointer = records; pointer != 0;)
            {
                var record = Marshal.PtrToStructure<RecordHeader>(pointer);
                if (record.Type == DnsTypeSrv && record.Length >= Marshal.SizeOf<SrvData>())
                {
                    var srv = Marshal.PtrToStructure<SrvData>(pointer + Marshal.SizeOf<RecordHeader>());
                    string? target = Marshal.PtrToStringUni(srv.Target)?.TrimEnd('.');
                    if ((best is null || srv.Priority < priority) && !string.IsNullOrWhiteSpace(target) && srv.Port > 0
                        && MinecraftServerAddress.TryParse(FormattableString.Invariant($"{target}:{srv.Port}"), out var result))
                    {
                        best = result;
                        priority = srv.Priority;
                    }
                }

                pointer = record.Next;
            }

            return best;
        }
        finally
        {
            if (records != 0) DnsFree(records, 1);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RecordHeader
    {
        internal readonly nint Next;
        private readonly nint _name;
        internal readonly ushort Type;
        internal readonly ushort Length;
        private readonly uint _flags;
        private readonly uint _ttl;
        private readonly uint _reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SrvData
    {
        internal readonly nint Target;
        internal readonly ushort Priority;
        private readonly ushort _weight;
        internal readonly ushort Port;
        private readonly ushort _padding;
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int DnsQuery(string name, ushort type, uint options, nint extra, out nint result, nint reserved);

    [DllImport("dnsapi.dll", ExactSpelling = true)]
    private static extern void DnsFree(nint data, int freeType);
}
