using System.Runtime.InteropServices;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// Reads the connected Wi-Fi signal through the Native Wifi API, whose result does not depend on the system language,
/// unlike parsing netsh output. Only the signal quality is taken; the network name in the same structure is never read.
/// </summary>
public static class WifiSignalReader
{
    private const uint CLIENT_VERSION = 2;
    private const uint ERROR_SUCCESS = 0;
    private const uint OPCODE_CURRENT_CONNECTION = 7;
    private const int INTERFACE_STATE_CONNECTED = 1;

    // Layout of WLAN_INTERFACE_INFO_LIST and WLAN_CONNECTION_ATTRIBUTES from wlanapi.h.
    private const int LIST_HEADER_BYTES = 8;
    private const int INTERFACE_INFO_BYTES = 532;
    private const int GUID_BYTES = 16;
    private const int INTERFACE_STATE_OFFSET = 528;
    private const int SIGNAL_QUALITY_OFFSET = 576;

    public static int? Read()
    {
        if (WlanOpenHandle(CLIENT_VERSION, IntPtr.Zero, out _, out var handle) != ERROR_SUCCESS)
        {
            return null;
        }

        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out var list) != ERROR_SUCCESS)
            {
                return null;
            }

            try
            {
                return ReadConnectedSignal(handle, list);
            }
            finally
            {
                WlanFreeMemory(list);
            }
        }
        finally
        {
            _ = WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    private static int? ReadConnectedSignal(IntPtr handle, IntPtr list)
    {
        var count = Marshal.ReadInt32(list);
        for (var index = 0; index < count; index++)
        {
            var entry = list + LIST_HEADER_BYTES + (index * INTERFACE_INFO_BYTES);
            if (Marshal.ReadInt32(entry, INTERFACE_STATE_OFFSET) != INTERFACE_STATE_CONNECTED)
            {
                continue;
            }

            var bytes = new byte[GUID_BYTES];
            Marshal.Copy(entry, bytes, 0, GUID_BYTES);
            var id = new Guid(bytes);
            if (WlanQueryInterface(handle, ref id, OPCODE_CURRENT_CONNECTION, IntPtr.Zero, out _, out var data, IntPtr.Zero) != ERROR_SUCCESS)
            {
                return null;
            }

            try
            {
                return Marshal.ReadInt32(data, SIGNAL_QUALITY_OFFSET);
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        return null;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr handle, ref Guid interfaceId, uint opcode, IntPtr reserved, out uint dataSize, out IntPtr data, IntPtr opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
