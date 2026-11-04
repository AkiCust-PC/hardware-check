using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Monitoring;

/// <summary>Collects samples from the sampler thread and tags each with the phase running at that moment.</summary>
public sealed class MonitoringSession
{
    private static readonly HashSet<string> RecordedTypes = ["Temperature", "Load", "Fan", "Power", "Clock"];

    private readonly Lock _gate = new();
    private readonly List<PhasedReading> _samples = [];
    private TestPhase _phase = TestPhase.Initialization;
    private IReadOnlyList<SensorReading> _latest = [];
    private string? _lastFailure;
    private int _readFailures;
    private int _readCount;

    public TestPhase CurrentPhase
    {
        get
        {
            lock (_gate)
            {
                return _phase;
            }
        }
    }

    public IReadOnlyList<SensorReading> Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public int ReadCount
    {
        get
        {
            lock (_gate)
            {
                return _readCount;
            }
        }
    }

    public int ReadFailures
    {
        get
        {
            lock (_gate)
            {
                return _readFailures;
            }
        }
    }

    public string? LastFailure
    {
        get
        {
            lock (_gate)
            {
                return _lastFailure;
            }
        }
    }

    public void BeginPhase(TestPhase phase)
    {
        lock (_gate)
        {
            _phase = phase;
        }
    }

    public void Record(IReadOnlyList<SensorReading> readings)
    {
        lock (_gate)
        {
            _latest = readings;
            _readCount++;
            foreach (var reading in readings)
            {
                if (RecordedTypes.Contains(reading.SensorType))
                {
                    _samples.Add(new PhasedReading(_phase, reading));
                }
            }
        }
    }

    public void RecordFailure(string message)
    {
        lock (_gate)
        {
            _readFailures++;
            _lastFailure = message;
        }
    }

    public IReadOnlyList<PhasedReading> Samples()
    {
        lock (_gate)
        {
            return [.. _samples];
        }
    }
}
