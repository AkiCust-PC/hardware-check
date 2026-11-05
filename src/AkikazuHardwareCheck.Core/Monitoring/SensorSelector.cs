using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Monitoring;

/// <summary>Null means that fan could not be read or has nothing attached.</summary>
public sealed record FanSpeeds(double? Cpu, double? Pump, double? Gpu, double? Case);

/// <summary>
/// Picks the sensors that represent a device for summaries, load checks and protection. Names come from LibreHardwareMonitor.
/// </summary>
public static class SensorSelector
{
    public static readonly IReadOnlyList<string> CpuTemperatureNames = ["CPU Package", "Core (Tctl/Tdie)", "Core Average", "Core Max"];
    private static readonly IReadOnlyList<string> GpuTemperatureNames = ["GPU Core"];
    private static readonly string[] GpuLoadNames = ["GPU Core", "D3D 3D", "D3D Compute"];
    private static readonly string[] CpuVoltageNames = ["CPU Core", "Core (SVI3 TFN)", "Core (SVI2 TFN)"];

    public static bool IsCpu(string hardwareType) => hardwareType == "Cpu";

    public static bool IsGpu(string hardwareType) => hardwareType.StartsWith("Gpu", StringComparison.Ordinal);

    /// <summary>
    /// Sensor hardware is matched to a DXGI adapter by name. Identical cards cannot be told apart this way, so all
    /// of them count: protection then reacts to the hottest card, which errs on the safe side.
    /// </summary>
    public static bool MatchesGpu(string hardwareType, string hardwareName, string? gpuModel) =>
        IsGpu(hardwareType)
        && gpuModel is not null
        && Normalize(hardwareName).Equals(Normalize(gpuModel), StringComparison.OrdinalIgnoreCase);

    /// <summary>The first listed name with a valid value wins, so one model keeps using one sensor throughout a run.</summary>
    public static string? CpuTemperatureName(IEnumerable<SensorReading> readings) =>
        FirstAvailableName(readings.Where(reading => IsCpu(reading.HardwareType) && IsValidType(reading, "Temperature")), CpuTemperatureNames);

    public static string? GpuTemperatureName(IEnumerable<SensorReading> readings, string? gpuModel) =>
        FirstAvailableName(GpuReadings(readings, gpuModel, "Temperature"), GpuTemperatureNames);

    public static double? CpuTemperature(IEnumerable<SensorReading> readings)
    {
        var snapshot = readings as IReadOnlyCollection<SensorReading> ?? [.. readings];
        var name = CpuTemperatureName(snapshot);
        return name is null ? null : snapshot.Where(reading => IsCpu(reading.HardwareType) && IsValidType(reading, "Temperature") && reading.SensorName == name).Max(reading => reading.Value);
    }

    public static double? GpuTemperature(IEnumerable<SensorReading> readings, string? gpuModel)
    {
        var snapshot = readings as IReadOnlyCollection<SensorReading> ?? [.. readings];
        var name = GpuTemperatureName(snapshot, gpuModel);
        return name is null ? null : GpuReadings(snapshot, gpuModel, "Temperature").Where(reading => reading.SensorName == name).Max(reading => reading.Value);
    }

    public static double? CpuLoad(IEnumerable<SensorReading> readings) =>
        readings.FirstOrDefault(reading => IsCpu(reading.HardwareType) && IsValidType(reading, "Load") && reading.SensorName == "CPU Total")?.Value;

    public static double? GpuLoad(IEnumerable<SensorReading> readings, string? gpuModel) =>
        GpuReadings(readings, gpuModel, "Load").Where(reading => IsGpuLoadName(reading.SensorName)).Max(reading => reading.Value);

    public static double? CpuVoltage(IEnumerable<SensorReading> readings)
    {
        var voltages = readings.Where(reading => IsCpu(reading.HardwareType) && IsValidType(reading, "Voltage") && reading.Value > 0).ToList();
        return CpuVoltageNames.Select(name => voltages.Where(reading => reading.SensorName == name).Max(reading => reading.Value)).FirstOrDefault(value => value is not null);
    }

    /// <summary>The fastest core: cores run at different clocks, and an unread sensor reports 0.</summary>
    public static double? CpuClock(IEnumerable<SensorReading> readings) => readings
        .Where(reading => IsCpu(reading.HardwareType) && IsValidType(reading, "Clock") && reading.SensorName.Contains("Core #", StringComparison.Ordinal) && reading.Value > 0)
        .Max(reading => reading.Value);

    public static double? GpuVoltage(IEnumerable<SensorReading> readings, string? gpuModel) =>
        GpuReadings(readings, gpuModel, "Voltage").Where(reading => reading.Value > 0).Max(reading => reading.Value);

    public static double? GpuClock(IEnumerable<SensorReading> readings, string? gpuModel) =>
        GpuReadings(readings, gpuModel, "Clock").Where(reading => reading.SensorName == "GPU Core" && reading.Value > 0).Max(reading => reading.Value);

    /// <summary>
    /// Fan speeds under the names desktop boards and laptop controllers use. A header with nothing attached reads 0 and is left
    /// out; a graphics card fan is kept at 0, because many cards stop their fans when cool.
    /// </summary>
    public static FanSpeeds Fans(IEnumerable<SensorReading> readings)
    {
        var fans = readings.Where(reading => IsValidType(reading, "Fan")).ToList();
        bool Has(SensorReading reading, string word) => reading.SensorName.Contains(word, StringComparison.OrdinalIgnoreCase);
        bool IsPump(SensorReading reading) => Has(reading, "pump");
        bool IsGpuFan(SensorReading reading) => IsGpu(reading.HardwareType) || Has(reading, "gpu");
        bool IsCpuFan(SensorReading reading) => Has(reading, "cpu") && !IsGpuFan(reading) && !IsPump(reading);
        return new FanSpeeds(
            fans.Where(reading => IsCpuFan(reading) && reading.Value > 0).Max(reading => reading.Value),
            fans.Where(reading => IsPump(reading) && reading.Value > 0).Max(reading => reading.Value),
            fans.Where(IsGpuFan).Max(reading => reading.Value),
            fans.Where(reading => !IsCpuFan(reading) && !IsGpuFan(reading) && !IsPump(reading) && reading.Value > 0).Max(reading => reading.Value));
    }

    public static bool IsGpuLoadName(string sensorName) =>
        GpuLoadNames.Any(name => sensorName.StartsWith(name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<SensorReading> GpuReadings(IEnumerable<SensorReading> readings, string? gpuModel, string sensorType) =>
        readings.Where(reading => MatchesGpu(reading.HardwareType, reading.HardwareName, gpuModel) && IsValidType(reading, sensorType));

    private static bool IsValidType(SensorReading reading, string sensorType) =>
        reading.SensorType == sensorType && reading is { Status: SensorStatus.Ok, Value: not null };

    private static string? FirstAvailableName(IEnumerable<SensorReading> readings, IReadOnlyList<string> names)
    {
        var available = readings.Select(reading => reading.SensorName).ToHashSet();
        return names.FirstOrDefault(available.Contains);
    }

    private static string Normalize(string name) =>
        name.Replace("(R)", "", StringComparison.OrdinalIgnoreCase).Replace("(TM)", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.Ordinal);
}
