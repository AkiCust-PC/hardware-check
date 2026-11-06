using System.Runtime.InteropServices;

namespace AkikazuHardwareCheck.Core.Diagnostics;

public sealed record EventSummary(string Name, int? Count);

/// <summary>
/// Counts a small set of stability-related System events. It reads event identifiers, not localized messages, so the
/// result does not depend on the Windows display language. This is a lead for a technician, not a diagnosis.
/// </summary>
public static class EventSummaryCollector
{
    public const int WINDOW_DAYS = 30;

    private const uint EVT_QUERY_CHANNEL_PATH = 0x1;
    private const uint EVT_QUERY_FORWARD_DIRECTION = 0x100;
    private const int EVENT_BATCH_SIZE = 64;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const long MILLISECONDS_PER_DAY = 86_400_000;

    private static readonly (string Name, string Provider, int? Id)[] Queries =
    [
        ("WHEA", "Microsoft-Windows-WHEA-Logger", null),
        ("Kernel-Power 41", "Microsoft-Windows-Kernel-Power", 41),
        ("BugCheck 1001", "Microsoft-Windows-WER-SystemErrorReporting", 1001),
        ("Display TDR 4101", "Display", 4101),
        ("Disk 7", "disk", 7),
        ("Ntfs 55", "Ntfs", 55),
    ];

    public static List<EventSummary> Read() =>
        Queries.Select(query => new EventSummary(query.Name, Count(query.Provider, query.Id))).ToList();

    private static int? Count(string provider, int? id)
    {
        var eventFilter = id is { } value ? $" and EventID={value}" : "";
        var path = $"*[System[Provider[@Name='{provider}']{eventFilter} and TimeCreated[timediff(@SystemTime) <= {WINDOW_DAYS * MILLISECONDS_PER_DAY}]]]";
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
