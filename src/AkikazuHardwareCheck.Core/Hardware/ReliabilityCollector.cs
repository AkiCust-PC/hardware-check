using System.Globalization;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Signs of hardware instability that no single sensor reading shows. None of these sources needs administrator rights.</summary>
public static class ReliabilityCollector
{
    public const int WHEA_WINDOW_DAYS = 30;

    private const string PROCESSOR_KEY = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string MICROCODE_VALUE = "Update Revision";
    private const int DWORD_BYTES = 4;
    private const long MILLISECONDS_PER_DAY = 86_400_000;

    // wevtapi.h: read a channel oldest first.
    private const uint EVT_QUERY_CHANNEL_PATH = 0x1;
    private const uint EVT_QUERY_FORWARD_DIRECTION = 0x100;
    private const int EVENT_BATCH_SIZE = 64;
    private const int ERROR_NO_MORE_ITEMS = 259;

    public static ReliabilityInfo Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PROCESSOR_KEY);
        var bootedAt = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return new ReliabilityInfo(CountWheaErrors(), ParseMicrocode(key?.GetValue(MICROCODE_VALUE) as byte[]), bootedAt);
    }

    /// <summary>Windows stores 4 bytes on some CPUs and 8 on others, where the revision sits in the upper half.</summary>
    public static string? ParseMicrocode(byte[]? value)
    {
        if (value is null || value.Length < DWORD_BYTES)
        {
            return null;
        }

        var upper = value.Length >= DWORD_BYTES * 2 ? BitConverter.ToUInt32(value, DWORD_BYTES) : 0;
        var revision = upper != 0 ? upper : BitConverter.ToUInt32(value, 0);
        return revision == 0 ? null : $"0x{revision.ToString("X", CultureInfo.InvariantCulture)}";
    }

    private static int? CountWheaErrors()
    {
        var path = $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= {WHEA_WINDOW_DAYS * MILLISECONDS_PER_DAY}]]]";
        var query = EvtQuery(IntPtr.Zero, "System", path, EVT_QUERY_CHANNEL_PATH | EVT_QUERY_FORWARD_DIRECTION);
        if (query == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var count = 0;
            var events = new IntPtr[EVENT_BATCH_SIZE];
            while (EvtNext(query, events.Length, events, -1, 0, out var returned))
            {
                count += returned;
                foreach (var item in events.Take(returned))
                {
                    EvtClose(item);
                }
            }

            return Marshal.GetLastPInvokeError() == ERROR_NO_MORE_ITEMS ? count : null;
        }
        finally
        {
            EvtClose(query);
        }
    }

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr EvtQuery(IntPtr session, string path, string query, uint flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern bool EvtNext(IntPtr results, int size, [Out] IntPtr[] events, int timeout, int flags, out int returned);

    [DllImport("wevtapi.dll")]
    private static extern bool EvtClose(IntPtr handle);
}
