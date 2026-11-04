using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.Core.Reporting;

/// <summary>
/// Power, clock and voltage under load, for the devices that were really loaded. Sensor names come from LibreHardwareMonitor,
/// which reports 0 rather than nothing for CPU sensors it may not read without administrator rights, so 0 counts as unread.
/// </summary>
public static class LoadMetricSummary
{
    private static readonly string[] CpuPowerNames = ["CPU Package", "Package"];
    private static readonly string[] GpuPowerNames = ["GPU Package", "GPU Power", "GPU Core"];
    private static readonly string[] CpuVoltageNames = ["CPU Core", "Core (SVI3 TFN)", "Core (SVI2 TFN)"];

    public static List<LoadMetricRow> Build(TestRunResult run)
    {
        var isCpuLoaded = run.Phases.Any(phase => phase is { Phase: TestPhase.CpuStress, ActualSeconds: > 0 });
        var loadedGpu = run.Phases.FirstOrDefault(phase => phase is { Phase: TestPhase.GpuStress, ActualSeconds: > 0 });
        var rows = new List<LoadMetricRow>();
        if (isCpuLoaded)
        {
            var cpu = Under(run, TestPhase.CpuStress).Where(item => SensorSelector.IsCpu(item.HardwareType)).ToList();
            rows.Add(Named(LoadMetric.CpuPower, "W", cpu.Where(item => item.SensorType == "Power"), CpuPowerNames));
            rows.Add(Clock(cpu));
            rows.Add(Named(LoadMetric.CpuVoltage, "V", cpu.Where(item => item.SensorType == "Voltage"), CpuVoltageNames));
        }

        if (loadedGpu is not null)
        {
            var gpu = Under(run, TestPhase.GpuStress).Where(item => item.SensorType == "Power" && SensorSelector.MatchesGpu(item.HardwareType, item.HardwareName, loadedGpu.TargetDevice));
            rows.Add(Named(LoadMetric.GpuPower, "W", gpu, GpuPowerNames));
        }

        // A sensor that could not be read is left out rather than shown as missing.
        return rows.Where(row => row.Maximum is not null).ToList();
    }

    public static string Text(Translator t, LoadMetricRow row) =>
        t["metric.value", Number(row.Average ?? 0, row.Unit), Number(row.Maximum ?? 0, row.Unit), row.Unit];

    private static IEnumerable<SensorStatistics> Under(TestRunResult run, TestPhase phase) =>
        run.Statistics.Where(item => (item.Phase == phase || item.Phase == TestPhase.DualStress) && item.Average is not null && item.Maximum > 0);

    private static LoadMetricRow Named(LoadMetric metric, string unit, IEnumerable<SensorStatistics> items, string[] names)
    {
        var snapshot = items.ToList();
        var chosen = names.Select(name => snapshot.Where(item => item.SensorName == name).MaxBy(item => item.Maximum)).FirstOrDefault(item => item is not null);
        return new LoadMetricRow(metric, chosen?.Average, chosen?.Maximum, unit);
    }

    /// <summary>Each core has its own clock sensor: the average runs across cores and the maximum is the fastest core's peak.</summary>
    private static LoadMetricRow Clock(List<SensorStatistics> cpu)
    {
        var cores = cpu.Where(item => item.SensorType == "Clock" && item.SensorName.Contains("Core #", StringComparison.Ordinal)).ToList();
        return cores.Count == 0
            ? new LoadMetricRow(LoadMetric.CpuClock, null, null, "MHz")
            : new LoadMetricRow(LoadMetric.CpuClock, cores.Average(item => item.Average!.Value), cores.Max(item => item.Maximum), "MHz");
    }

    private static string Number(double value, string unit) =>
        value.ToString(unit == "V" ? "0.000" : "0", CultureInfo.InvariantCulture);
}
