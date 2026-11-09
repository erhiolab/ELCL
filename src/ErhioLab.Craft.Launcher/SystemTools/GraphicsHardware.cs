using System.Runtime.InteropServices;
using System.Text;

namespace ErhioLab.Craft.Launcher.SystemTools;

internal enum GraphicsAdapterKind
{
    Integrated,
    Discrete
}

internal sealed record GraphicsAdapterInfo(string Name, GraphicsAdapterKind Kind);

/// <summary>通过 DXCore 枚举硬件图形适配器.</summary>
internal static class GraphicsHardware
{
    private static readonly Guid D3D11Graphics = new("8c47866b-7583-450d-f0f0-6bada895af4b");
    private static readonly Guid D3D12Graphics = new("0c9ece4d-2f6e-4f01-8c96-e89e331b47b1");

    internal static GraphicsAdapterInfo[] Read()
    {
        Guid factoryId = typeof(IAdapterFactory).GUID;
        Marshal.ThrowExceptionForHR(DXCoreCreateAdapterFactory(ref factoryId, out var factory));
        try
        {
            var adapters = new List<GraphicsAdapterInfo>();
            var seen = new HashSet<ulong>();
            foreach (Guid graphicsAttribute in new[] { D3D12Graphics, D3D11Graphics })
            {
                Guid listId = typeof(IAdapterList).GUID;
                Guid attribute = graphicsAttribute;
                Marshal.ThrowExceptionForHR(factory.CreateAdapterList(1, ref attribute, ref listId, out var list));
                try
                {
                    Guid adapterId = typeof(IAdapter).GUID;
                    uint count = list.GetAdapterCount();
                    for (uint index = 0; index < count; index++)
                    {
                        Marshal.ThrowExceptionForHR(list.GetAdapter(index, ref adapterId, out var adapter));
                        try
                        {
                            if (!adapter.IsValid() || !ReadBoolean(adapter, AdapterProperty.IsHardware)
                                                   || !SupportsGraphics(adapter)) continue;
                            ulong luid = BitConverter.ToUInt64(ReadProperty(adapter, AdapterProperty.InstanceLuid));
                            if (!seen.Add(luid)) continue;
                            string name = Encoding.UTF8.GetString(ReadProperty(adapter, AdapterProperty.DriverDescription)).TrimEnd('\0').Trim();
                            if (name.Length == 0) continue;
                            bool integrated = ReadBoolean(adapter, AdapterProperty.IsIntegrated);
                            adapters.Add(new(name, integrated ? GraphicsAdapterKind.Integrated : GraphicsAdapterKind.Discrete));
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(adapter);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(list);
                }
            }

            return adapters.OrderBy(adapter => adapter.Kind).ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    private static bool SupportsGraphics(IAdapter adapter)
    {
        Guid graphics11 = D3D11Graphics, graphics12 = D3D12Graphics;
        return adapter.IsAttributeSupported(ref graphics11) || adapter.IsAttributeSupported(ref graphics12);
    }

    private static bool ReadBoolean(IAdapter adapter, AdapterProperty property) => ReadProperty(adapter, property)[0] != 0;

    private static byte[] ReadProperty(IAdapter adapter, AdapterProperty property)
    {
        if (!adapter.IsPropertySupported(property)) throw new NotSupportedException("显卡驱动不支持读取属性: " + property);
        Marshal.ThrowExceptionForHR(adapter.GetPropertySize(property, out nuint size));
        if (size is 0 or > 65536) throw new InvalidOperationException("显卡属性数据大小无效.");
        nint buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            Marshal.ThrowExceptionForHR(adapter.GetProperty(property, size, buffer));
            byte[] bytes = new byte[checked((int)size)];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // DXCoreAdapterProperty 的值与 Windows SDK 的 dxcore_interface.h 保持一致.
    private enum AdapterProperty
    {
        InstanceLuid = 0,
        DriverDescription = 2,
        IsHardware = 11,
        IsIntegrated = 12
    }

    [DllImport("dxcore.dll", ExactSpelling = true)]
    private static extern int DXCoreCreateAdapterFactory(ref Guid iid, out IAdapterFactory factory);

    // 仅声明用到的前部 vtable 槽位; bool 在 DXCore 中占一个字节.
    [ComImport, Guid("78ee5945-c36e-4b13-a669-005dd11c0f06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapterFactory
    {
        [PreserveSig]
        int CreateAdapterList(uint attributeCount, ref Guid attribute, ref Guid iid, out IAdapterList list);
    }

    [ComImport, Guid("526c7776-40e9-459b-b711-f32ad76dfc28"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapterList
    {
        [PreserveSig]
        int GetAdapter(uint index, ref Guid iid, out IAdapter adapter);

        [PreserveSig]
        uint GetAdapterCount();
    }

    [ComImport, Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapter
    {
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)]
        bool IsValid();

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)]
        bool IsAttributeSupported(ref Guid attribute);

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)]
        bool IsPropertySupported(AdapterProperty property);

        [PreserveSig]
        int GetProperty(AdapterProperty property, nuint size, nint data);

        [PreserveSig]
        int GetPropertySize(AdapterProperty property, out nuint size);
    }
}
