namespace AkikazuHardwareCheck.Core.Models;

public enum TestPhase
{
    Initialization,
    Idle,
    GpuStress,
    CpuStress,
    DualStress,
    Finished,
}

/// <summary>One sample: the sensor's device, id, type, value, unit, source and time, plus the phase it belongs to.</summary>
public sealed record PhasedReading(TestPhase Phase, SensorReading Reading);

/// <summary>Statistics for a single sensor in a single phase. Sensors are never merged, so Package and Tctl stay apart.</summary>
public sealed record SensorStatistics(
    TestPhase Phase,
    string HardwareId,
    string HardwareType,
    string HardwareName,
    string SensorId,
    string SensorName,
    string SensorType,
    string Unit,
    double? Minimum,
    double? Maximum,
    double? Average,
    int ValidSamples,
    int TotalSamples,
    int ExpectedSamples,
    double? LongestGapSeconds,
    bool IsSufficient);
