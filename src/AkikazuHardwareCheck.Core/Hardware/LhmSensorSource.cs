using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;
using LibreHardwareMonitor.Hardware;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Reads sensors through LibreHardwareMonitorLib. The computer stays open between reads because opening takes seconds.</summary>
public sealed class LhmSensorSource : ISensorSource
{
    private const string SENSOR_SOURCE = "LibreHardwareMonitorLib";

    // Load sensors report nothing until a second update gives them a time window to measure.
    private const int SETTLE_INTERVAL_MS = 1000;

    private const double MIN_VALID_TEMPERATURE_C = 1;
    private const double MAX_VALID_TEMPERATURE_C = 150;
    private const double MAX_VALID_FAN_RPM = 20000;

    private readonly UpdateVisitor _visitor = new();
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        // USB liquid coolers report their pump speed through controllers rather than the motherboard.
        IsControllerEnabled = true,
        IsStorageEnabled = true,
    };

    private bool _isOpen;

    /// <summary>Takes one settled snapshot and closes the sensors again.</summary>
    public static IReadOnlyList<SensorReading> ReadOnce()
    {
        using var source = new LhmSensorSource();
        source.Read();
        Thread.Sleep(SETTLE_INTERVAL_MS);
        return source.Read();
    }

    public IReadOnlyList<SensorReading> Read()
    {
        if (!_isOpen)
        {
            _computer.Open();
            _isOpen = true;
        }

        _computer.Accept(_visitor);
        var sampledAt = DateTimeOffset.Now;
        return _computer.Hardware.SelectMany(hardware => ReadHardware(hardware, sampledAt)).ToList();
    }

    public void Dispose()
    {
        if (_isOpen)
        {
            _computer.Close();
            _isOpen = false;
        }
    }

    public static SensorStatus Classify(SensorType type, double? value)
    {
        if (value is null)
        {
            return SensorStatus.NoValue;
        }

        if (!double.IsFinite(value.Value))
        {
            return SensorStatus.Invalid;
        }

        return type switch
        {
            SensorType.Temperature when value is < MIN_VALID_TEMPERATURE_C or > MAX_VALID_TEMPERATURE_C => SensorStatus.Invalid,
            SensorType.Fan when value is < 0 or > MAX_VALID_FAN_RPM => SensorStatus.Invalid,
            _ => SensorStatus.Ok,
        };
    }

    private static IEnumerable<SensorReading> ReadHardware(IHardware hardware, DateTimeOffset sampledAt)
    {
        var own = hardware.Sensors.Select(sensor => ToReading(hardware, sensor, sampledAt));
        var nested = hardware.SubHardware.SelectMany(child => ReadHardware(child, sampledAt));
        return own.Concat(nested);
    }

    private static SensorReading ToReading(IHardware hardware, ISensor sensor, DateTimeOffset sampledAt)
    {
        double? value = sensor.Value;
        var status = Classify(sensor.SensorType, value);
        return new SensorReading(
            hardware.Identifier.ToString(),
            hardware.Name,
            hardware.HardwareType.ToString(),
            sensor.Identifier.ToString(),
            sensor.Name,
            sensor.SensorType.ToString(),
            value is { } number && double.IsFinite(number) ? number : null,
            UnitOf(sensor.SensorType),
            status,
            SENSOR_SOURCE,
            sampledAt);
    }

    private static string UnitOf(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Fan => "RPM",
        SensorType.Load or SensorType.Control or SensorType.Level => "%",
        SensorType.Clock => "MHz",
        SensorType.Power => "W",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.Frequency => "Hz",
        _ => "",
    };

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}