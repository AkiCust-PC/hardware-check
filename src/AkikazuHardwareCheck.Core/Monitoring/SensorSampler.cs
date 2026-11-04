using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Monitoring;

/// <summary>
/// Reads sensors on its own raised-priority thread, so measurements keep arriving while the CPU test saturates the machine.
/// </summary>
public sealed class SensorSampler(ISensorSource source, MonitoringSession session, TimeSpan interval) : IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly ManualResetEventSlim _stop = new();
    private Thread? _thread;

    /// <summary>Raised on the sampler thread after each successful read. Handlers must return quickly.</summary>
    public event Action<IReadOnlyList<SensorReading>>? Sampled;

    public void Start()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Sensor sampler",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        _stop.Set();
        _thread?.Join(StopTimeout);
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }

    private void Loop()
    {
        while (!_stop.IsSet)
        {
            var startedAt = DateTime.UtcNow;
            ReadOnce();
            var remaining = interval - (DateTime.UtcNow - startedAt);
            if (remaining > TimeSpan.Zero)
            {
                _stop.Wait(remaining);
            }
        }
    }

    private void ReadOnce()
    {
        // A failed read is a measurement gap, which statistics report; it must never end the session.
        try
        {
            var readings = source.Read();
            session.Record(readings);
            Sampled?.Invoke(readings);
        }
        catch (Exception exception)
        {
            session.RecordFailure(exception.Message);
        }
    }
}
