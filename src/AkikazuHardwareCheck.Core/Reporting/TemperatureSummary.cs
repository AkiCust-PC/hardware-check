using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.Core.Reporting;

/// <summary>Picks the four customer-facing rows: CPU and GPU, each at idle and under its own load.</summary>
public static class TemperatureSummary
{
    public static List<TemperatureRow> Build(TestRunResult run, GpuInfo? targetGpu)
    {
        var target = targetGpu ?? run.Phases.Where(phase => phase.TargetDevice is not null)
            .Select(phase => new GpuInfo(null, phase.TargetDevice, null, null, GpuKind.Unknown, null))
            .FirstOrDefault();

        return
        [
            CpuRow(run, TestPhase.Idle),
            CpuRow(run, TestPhase.CpuStress),
            GpuRow(run, TestPhase.Idle, target),
            GpuRow(run, TestPhase.GpuStress, target),
        ];
    }

    /// <summary>A dual test loads both devices at once, so its samples stand for each device under load.</summary>
    private static bool InPhase(SensorStatistics item, TestPhase phase) =>
        item.Phase == phase || (phase != TestPhase.Idle && item.Phase == TestPhase.DualStress);

    private static TemperatureRow CpuRow(TestRunResult run, TestPhase phase)
    {
        var temperatures = run.Statistics.Where(item => InPhase(item, phase) && item.SensorType == "Temperature" && SensorSelector.IsCpu(item.HardwareType));
        var chosen = SensorSelector.CpuTemperatureNames
            .Select(name => temperatures.FirstOrDefault(item => item.SensorName == name && item.Average is not null))
            .FirstOrDefault(item => item is not null);
        return ToRow(run, phase, true, chosen);
    }

    private static TemperatureRow GpuRow(TestRunResult run, TestPhase phase, GpuInfo? target)
    {
        // With identical cards the hottest stands for the target, matching the protection logic.
        var chosen = run.Statistics
            .Where(item => InPhase(item, phase) && item.SensorType == "Temperature" && item.SensorName == "GPU Core" && item.Average is not null
                && SensorSelector.MatchesGpu(item.HardwareType, item.HardwareName, target?.Model))
            .MaxBy(item => item.Maximum);
        return ToRow(run, phase, false, chosen);
    }

    private static TemperatureRow ToRow(TestRunResult run, TestPhase phase, bool isCpu, SensorStatistics? statistics)
    {
        var result = run.Phases.FirstOrDefault(item => item.Phase == phase);
        if (result is null
            || result.Status == TestStatus.NotRun
            || ((result.Status is TestStatus.Unsupported or TestStatus.Cancelled or TestStatus.ExecutionFailed) && result.ActualSeconds == 0))
        {
            return new TemperatureRow(isCpu, phase, null, null, null, null, 0, TemperatureRowState.NotRun);
        }

        if (statistics is null)
        {
            return new TemperatureRow(isCpu, phase, null, null, null, null, 0, TemperatureRowState.Unavailable);
        }

        return new TemperatureRow(
            isCpu,
            phase,
            statistics.SensorName,
            statistics.Minimum,
            statistics.Maximum,
            statistics.Average,
            statistics.ValidSamples,
            statistics.IsSufficient ? TemperatureRowState.Measured : TemperatureRowState.InsufficientData);
    }
}
