using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Monitoring;

public static class SensorStatisticsCalculator
{
    /// <summary>Recorded in reports, as the specification asks when sampling intervals may be uneven.</summary>
    public const string AVERAGE_METHOD = "ArithmeticMeanOfValidSamples";

    // A sensor that answered under half of the expected reads cannot represent the phase.
    public const double MIN_VALID_SAMPLE_RATIO = 0.5;

    /// <param name="samples">The session's samples.</param>
    /// <param name="phaseSeconds">How long each phase really ran, used for the expected sample count.</param>
    /// <param name="interval">The sampling interval.</param>
    public static List<SensorStatistics> Calculate(
        IEnumerable<PhasedReading> samples,
        IReadOnlyDictionary<TestPhase, double> phaseSeconds,
        TimeSpan interval)
    {
        return samples
            .GroupBy(sample => (sample.Phase, sample.Reading.SensorId))
            .Select(group => Summarize(group.Key.Phase, group.Select(sample => sample.Reading).ToList(), ExpectedSamples(group.Key.Phase, phaseSeconds, interval)))
            .ToList();
    }

    private static int ExpectedSamples(TestPhase phase, IReadOnlyDictionary<TestPhase, double> phaseSeconds, TimeSpan interval) =>
        phaseSeconds.TryGetValue(phase, out var seconds)
            ? Math.Max(1, (int)Math.Floor(seconds / interval.TotalSeconds))
            : 1;

    private static SensorStatistics Summarize(TestPhase phase, List<SensorReading> readings, int expected)
    {
        var first = readings[0];
        var valid = readings.Where(reading => reading is { Status: SensorStatus.Ok, Value: not null }).ToList();
        var values = valid.Select(reading => reading.Value!.Value).ToList();
        var hasValues = values.Count > 0;

        return new SensorStatistics(
            phase,
            first.HardwareId,
            first.HardwareType,
            first.HardwareName,
            first.SensorId,
            first.SensorName,
            first.SensorType,
            first.Unit,
            hasValues ? values.Min() : null,
            hasValues ? values.Max() : null,
            hasValues ? values.Average() : null,
            values.Count,
            readings.Count,
            expected,
            LongestGap(valid),
            hasValues && values.Count >= expected * MIN_VALID_SAMPLE_RATIO);
    }

    private static double? LongestGap(List<SensorReading> valid)
    {
        if (valid.Count < 2)
        {
            return null;
        }

        return valid.Zip(valid.Skip(1), (earlier, later) => (later.SampledAt - earlier.SampledAt).TotalSeconds).Max();
    }
}
