using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// Gives staff a cooling hint from fan sensors. It is an inference only: the confirmed cooling type comes from staff.
/// </summary>
public static class CoolingEstimator
{
    private const string COOLER_HARDWARE_TYPE = "Cooler";

    private static readonly HashSet<string> CoolingHardwareTypes =
    [
        "Motherboard",
        "SuperIO",
        "EmbeddedController",
        "Cooler",
    ];

    public static CoolingEstimate Estimate(IEnumerable<SensorReading> sensors, bool isSensorAccessLimited)
    {
        var readings = sensors as IReadOnlyCollection<SensorReading> ?? [.. sensors];
        var runningFans = readings.Where(IsRunningCoolingFan).ToList();
        var pumpRpm = FastestMatching(runningFans, IsPump);
        var cpuFanRpm = FastestMatching(runningFans, IsCpuFan);
        var unlabeledFans = runningFans.Count(fan => !IsPump(fan) && !IsCpuFan(fan) && IsGenericFanName(fan));

        var outcome = (pumpRpm, cpuFanRpm) switch
        {
            (not null, not null) => CoolingOutcome.LikelyLiquid,
            (null, not null) => CoolingOutcome.LikelyAir,
            (not null, null) => CoolingOutcome.AbnormalWiring,
            _ => CoolingOutcome.NoData,
        };

        return new CoolingEstimate(outcome, pumpRpm, cpuFanRpm, unlabeledFans, BuildNotes(outcome, unlabeledFans, isSensorAccessLimited), CoolerName(readings));
    }

    /// <summary>USB liquid coolers report their own product name. Other coolers have no interface, so nothing is shown for them.</summary>
    private static string? CoolerName(IEnumerable<SensorReading> readings) =>
        readings.FirstOrDefault(reading => reading.HardwareType == COOLER_HARDWARE_TYPE)?.HardwareName;

    private static List<CoolingNote> BuildNotes(CoolingOutcome outcome, int unlabeledFans, bool isSensorAccessLimited)
    {
        var notes = new List<CoolingNote>();
        if (outcome == CoolingOutcome.LikelyLiquid)
        {
            notes.Add(CoolingNote.PumpHeaderIsNotProof);
        }

        // A pump wired to CPU_FAN, or one not wired to the board, looks like air cooling to these sensors.
        if (outcome == CoolingOutcome.LikelyAir)
        {
            notes.Add(CoolingNote.PumpMayBeUnreported);
        }

        if (outcome == CoolingOutcome.AbnormalWiring)
        {
            notes.Add(CoolingNote.CheckCoolerWiring);
        }

        // Fanless systems have no RPM to report, so missing data is not a fault by itself.
        if (outcome == CoolingOutcome.NoData)
        {
            notes.Add(CoolingNote.PassiveCoolingPossible);
        }

        if (unlabeledFans > 0)
        {
            notes.Add(CoolingNote.UnlabeledFansPresent);
        }

        if (isSensorAccessLimited)
        {
            notes.Add(CoolingNote.SensorAccessLimited);
        }

        return notes;
    }

    private static bool IsRunningCoolingFan(SensorReading sensor) =>
        sensor.SensorType == "Fan"
        && sensor.Status == SensorStatus.Ok
        && sensor.Value > 0
        && CoolingHardwareTypes.Contains(sensor.HardwareType);

    private static bool IsPump(SensorReading sensor) =>
        sensor.SensorName.Contains("pump", StringComparison.OrdinalIgnoreCase);

    // "CPU Optional Fan" is a secondary header and does not show that the CPU cooler has a fan.
    private static bool IsCpuFan(SensorReading sensor) =>
        sensor.SensorName.Contains("cpu", StringComparison.OrdinalIgnoreCase)
        && sensor.SensorName.Contains("fan", StringComparison.OrdinalIgnoreCase)
        && !sensor.SensorName.Contains("opt", StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericFanName(SensorReading sensor) =>
        sensor.SensorName.StartsWith("Fan #", StringComparison.OrdinalIgnoreCase);

    private static double? FastestMatching(IEnumerable<SensorReading> fans, Func<SensorReading, bool> predicate) =>
        fans.Where(predicate).Max(fan => fan.Value);
}
