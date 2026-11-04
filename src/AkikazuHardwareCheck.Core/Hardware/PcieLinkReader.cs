using System.Runtime.InteropServices;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Reads the PCIe link a device really negotiated, by walking up from it to its PCI controller in the device tree.</summary>
internal static class PcieLinkReader
{
    private const int CR_SUCCESS = 0;
    private const int MAX_TREE_DEPTH = 8;

    // DEVPKEY_PciDevice_CurrentLinkSpeed and CurrentLinkWidth from Windows pciprop.h. The speed value is the PCIe generation.
    private static readonly Guid PciProperties = new("3AB22E31-8264-4B4E-9AF5-A8D2D8E33E62");
    private const uint CURRENT_LINK_SPEED_ID = 9;
    private const uint CURRENT_LINK_WIDTH_ID = 10;

    public static (int? Generation, int? Lanes) Read(string deviceInstanceId)
    {
        if (CM_Locate_DevNodeW(out var node, deviceInstanceId, 0) != CR_SUCCESS)
        {
            return (null, null);
        }

        for (var depth = 0; depth < MAX_TREE_DEPTH; depth++)
        {
            if (ReadValue(node, CURRENT_LINK_SPEED_ID) is { } generation and > 0)
            {
                return (generation, ReadValue(node, CURRENT_LINK_WIDTH_ID));
            }

            if (CM_Get_Parent(out var parent, node, 0) != CR_SUCCESS)
            {
                break;
            }

            node = parent;
        }

        return (null, null);
    }

    private static int? ReadValue(uint node, uint propertyId)
    {
        var key = new DevicePropertyKey { Category = PciProperties, PropertyId = propertyId };
        var size = (uint)sizeof(uint);
        return CM_Get_DevNode_PropertyW(node, in key, out _, out var value, ref size, 0) == CR_SUCCESS ? (int)value : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevicePropertyKey
    {
        public Guid Category;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(uint node, in DevicePropertyKey key, out uint propertyType, out uint value, ref uint size, uint flags);
}
