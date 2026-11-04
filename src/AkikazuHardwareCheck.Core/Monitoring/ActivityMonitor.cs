using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Hardware;

namespace AkikazuHardwareCheck.Core.Monitoring;

public sealed record ActivitySnapshot(int? MemoryLoadPercent, double? DiskBusyPercent, double? UploadBytesPerSecond, double? DownloadBytesPerSecond);

/// <summary>
/// Reads memory, disk and network activity once a second from Windows itself. None of these need administrator rights,
/// and the disk figure is the same "active time" Task Manager shows. Each source can fail alone and then reads as null.
/// </summary>
public sealed class ActivityMonitor : IDisposable
{
    private const string DISK_IDLE_COUNTER = @"\PhysicalDisk(_Total)\% Idle Time";
    private const uint PDH_FMT_DOUBLE = 0x200;
    private const double FULL_PERCENT = 100;
    private const int ADAPTER_REFRESH_TICKS = 5;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ManualResetEventSlim _stop = new();
    private Thread? _thread;
    private IntPtr _query;
    private IntPtr _counter;
    private List<NetworkInterface> _adapters = [];
    private int _ticksUntilRefresh;
    private long? _lastSent;
    private long? _lastReceived;
    private DateTime _lastSampleAt;

    /// <summary>Raised on the monitor's own thread.</summary>
    public event Action<ActivitySnapshot>? Sampled;

    /// <summary>Counters can restart when an adapter reconnects, which must not read as a huge negative speed.</summary>
    public static double? RatePerSecond(long? previous, long current, double seconds)
    {
        if (previous is not { } start || seconds <= 0)
        {
            return null;
        }

        return Math.Max(0, current - start) / seconds;
    }

    public static double BusyFromIdle(double idlePercent) => Math.Clamp(FULL_PERCENT - idlePercent, 0, FULL_PERCENT);

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Activity monitor" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(Interval * 2);
        if (_query != IntPtr.Zero)
        {
            _ = PdhCloseQuery(_query);
        }

        _stop.Dispose();
    }

    private void Loop()
    {
        OpenDiskCounter();
        while (!_stop.IsSet)
        {
            var (upload, download) = ReadNetwork();
            Sampled?.Invoke(new ActivitySnapshot(ReadMemoryLoad(), ReadDiskBusy(), upload, download));
            _stop.Wait(Interval);
        }
    }

    private void OpenDiskCounter()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
        {
            return;
        }

        // The English API keeps this working on every Windows language.
        if (PdhAddEnglishCounterW(_query, DISK_IDLE_COUNTER, IntPtr.Zero, out _counter) == 0)
        {
            _ = PdhCollectQueryData(_query);
        }
    }

    private double? ReadDiskBusy()
    {
        if (_counter == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
        {
            return null;
        }

        return PdhGetFormattedCounterValue(_counter, PDH_FMT_DOUBLE, out _, out var value) == 0 && value.Status == 0
            ? BusyFromIdle(value.Value)
            : null;
    }

    private static int? ReadMemoryLoad()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? (int)status.MemoryLoad : null;
    }

    private (double? Upload, double? Download) ReadNetwork()
    {
        try
        {
            if (_ticksUntilRefresh-- <= 0)
            {
                _adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                        && adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                        && NetworkCollector.IsPhysical(adapter.Description))
                    .ToList();
                _ticksUntilRefresh = ADAPTER_REFRESH_TICKS;
            }

            var sent = 0L;
            var received = 0L;
            foreach (var adapter in _adapters)
            {
                var statistics = adapter.GetIPStatistics();
                sent += statistics.BytesSent;
                received += statistics.BytesReceived;
            }

            var now = DateTime.UtcNow;
            var seconds = (now - _lastSampleAt).TotalSeconds;
            var result = (RatePerSecond(_lastSent, sent, seconds), RatePerSecond(_lastReceived, received, seconds));
            (_lastSent, _lastReceived, _lastSampleAt) = (sent, received, now);
            return result;
        }
        catch (NetworkInformationException)
        {
            return (null, null);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public double Value;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
