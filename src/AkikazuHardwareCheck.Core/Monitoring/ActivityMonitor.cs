using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Hardware;

namespace AkikazuHardwareCheck.Core.Monitoring;

public sealed record ActivitySnapshot(
    int? MemoryLoadPercent,
    double? DiskBusyPercent,
    double? UploadBytesPerSecond,
    double? DownloadBytesPerSecond,
    string? NetworkAdapter = null,
    double? LatencyMilliseconds = null);

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
    private const int LATENCY_REFRESH_TICKS = 5;
    private const int LATENCY_TIMEOUT_MS = 300;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ManualResetEventSlim _stop = new();
    private Thread? _thread;
    private IntPtr _query;
    private IntPtr _counter;
    private NetworkInterface? _adapter;
    private string? _adapterName;
    private int _ticksUntilRefresh;
    private int _ticksUntilLatency;
    private double? _latencyMilliseconds;
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
        var stopped = _thread is null || _thread.Join(Interval * 2);
        // Closing the PDH query or freeing the event while the monitor thread is still in it would be a use-after-free.
        // If the join timed out, the process is exiting anyway and the OS reclaims both handles.
        if (!stopped)
        {
            return;
        }

        if (_query != IntPtr.Zero)
        {
            _ = PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }

        _stop.Dispose();
    }

    private void Loop()
    {
        OpenDiskCounter();
        while (!_stop.IsSet)
        {
            var (upload, download) = ReadNetwork();
            var latency = ReadLatency();
            try
            {
                Sampled?.Invoke(new ActivitySnapshot(ReadMemoryLoad(), ReadDiskBusy(), upload, download, _adapterName, latency));
            }
            catch (Exception exception)
            {
                // A subscriber must not terminate the monitor thread; the next tick can still provide data.
                System.Diagnostics.Trace.TraceWarning(exception.Message);
            }

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

    /// <summary>One representative connection, so a Wi-Fi and Ethernet pair is never added together.</summary>
    private static NetworkInterface? ChooseAdapter()
    {
        try
        {
            var connected = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                    && NetworkCollector.IsPhysical(adapter.Description)
                    && HasGateway(adapter))
                .ToList();
            return connected.FirstOrDefault(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                ?? connected.FirstOrDefault(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static bool HasGateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses.Count > 0;
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private (double? Upload, double? Download) ReadNetwork()
    {
        try
        {
            if (_ticksUntilRefresh-- <= 0)
            {
                var previous = _adapterName;
                _adapter = ChooseAdapter();
                _adapterName = _adapter?.Description;
                if (_adapterName != previous)
                {
                    // Counters belong to one adapter. Reusing them after a switch would look like a huge burst.
                    _lastSent = null;
                    _lastReceived = null;
                }

                _ticksUntilRefresh = ADAPTER_REFRESH_TICKS;
            }

            if (_adapter is null)
            {
                return (null, null);
            }

            var statistics = _adapter.GetIPStatistics();
            var sent = statistics.BytesSent;
            var received = statistics.BytesReceived;

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

    /// <summary>
    /// Round-trip time to the default gateway, refreshed every few seconds. This is the local network path only; it is
    /// not the game server's latency and must not be presented as one.
    /// </summary>
    private double? ReadLatency()
    {
        if (_ticksUntilLatency-- > 0)
        {
            return _latencyMilliseconds;
        }

        _ticksUntilLatency = LATENCY_REFRESH_TICKS;
        try
        {
            var gateway = _adapter?.GetIPProperties().GatewayAddresses.FirstOrDefault()?.Address;
            if (gateway is null || gateway.Equals(IPAddress.Any) || gateway.Equals(IPAddress.IPv6Any))
            {
                _latencyMilliseconds = null;
                return null;
            }

            using var ping = new Ping();
            var reply = ping.Send(gateway, LATENCY_TIMEOUT_MS);
            _latencyMilliseconds = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception exception) when (exception is NetworkInformationException or PingException or InvalidOperationException or PlatformNotSupportedException or ArgumentException)
        {
            _latencyMilliseconds = null;
        }

        return _latencyMilliseconds;
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
