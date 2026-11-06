using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Probe;

public sealed class ConsoleReport(Translator t, TextWriter output)
{
    private const double BYTES_PER_GIB = 1024d * 1024 * 1024;
    private const double BYTES_PER_GB = 1000d * 1000 * 1000;

    private string Unavailable => t["common.unavailable"];

    public void Write(CheckReport report)
    {
        output.WriteLine(t["app.title", report.ToolVersion]);
        WriteEnvironment(report.Hardware.Environment);

        WriteSection(t["section.cpu"], report.Hardware.Cpus, cpu =>
            t["cpu.summary", Text(cpu.Model), Text(cpu.PhysicalCores?.ToString()), Text(cpu.LogicalThreads?.ToString())]);

        WriteSection(t["section.gpu"], report.Hardware.Gpus, gpu =>
            t["gpu.summary", Text(gpu.Model), Gib(gpu.DedicatedMemoryBytes), Text(gpu.DriverVersion)]
            + (gpu.IsSoftwareAdapter ? t["gpu.software"] : ""));

        WriteSection<MotherboardInfo>(t["section.motherboard"], report.Hardware.Motherboard is { } board ? [board] : [], item =>
            $"{Text(item.Manufacturer)} {Text(item.Model)} | BIOS {Text(item.BiosVersion)} ({Text(item.BiosDate)})");

        WriteMemory(report.Hardware.Memory);

        WriteSection(t["section.disks"], report.Hardware.Disks, disk =>
            t["disk.summary", Text(disk.Model), Gb(disk.SizeBytes), Text(disk.MediaType), Text(disk.BusType), Text(disk.HealthStatus)]
            + (disk.IsSystemDisk ? t["disk.system"] : ""));

        WriteSection(t["section.displays"], report.Hardware.Displays, FormatDisplay);

        WriteSensors(report.Sensors);
        WriteCooling(report.Cooling);

        if (report.TestRun is { } testRun)
        {
            WriteTestRun(testRun);
        }

        if (report.Hardware.Diagnostics.Count > 0)
        {
            WriteSection(t["section.diagnostics"], report.Hardware.Diagnostics, entry => $"{entry.Source}: {entry.Message}");
        }
    }

    private void WriteEnvironment(ProbeEnvironment environment)
    {
        output.WriteLine(t["environment.system", FormatSystem(environment.OperatingSystem)]);
        output.WriteLine(t["environment.activation", ActivationText(environment.Activation)]);
        output.WriteLine(t["environment.access", YesNo(environment.IsAdministrator), YesNo(environment.IsPawnIoInstalled)]);
        if (!environment.IsSensorAccessLimited)
        {
            return;
        }

        output.WriteLine(t["environment.limited"]);
        if (!environment.IsAdministrator)
        {
            output.WriteLine(t["environment.needAdministrator"]);
        }

        if (!environment.IsPawnIoInstalled)
        {
            output.WriteLine(t["environment.needPawnIo"]);
        }
    }

    private string FormatSystem(OperatingSystemInfo? system)
    {
        if (system is null)
        {
            return Unavailable;
        }

        var build = system.Build is null ? null : $"Build {system.Build}";
        string?[] parts = [Text(system.Name), system.DisplayVersion, build, system.Architecture];
        return string.Join(" | ", parts.Where(part => part is not null));
    }

    private void WriteMemory(MemoryInfo? memory)
    {
        var lines = new List<string>();
        if (memory is not null)
        {
            lines.Add(t["memory.summary", Gib(memory.InstalledBytes), Gib(memory.SystemVisibleBytes)]);
            lines.AddRange(memory.Modules.Select(module => t[
                "memory.module",
                Text(module.Slot),
                Gib(module.CapacityBytes),
                Text(module.MemoryType),
                Text(module.Manufacturer),
                Text(module.PartNumber),
                Mts(module.RatedSpeedMts),
                Mts(module.ConfiguredSpeedMts)]));
        }

        WriteSection(t["section.memory"], lines, line => line);
    }

    private string FormatDisplay(DisplayInfo display)
    {
        var name = Text(display.Name) + (display.ManufacturerCode is null ? "" : $" ({display.ManufacturerCode}{display.ProductCode})");
        var mode = $"{Resolution(display)} @ {(display.RefreshRateHz is { } rate ? $"{rate.ToString("0.#", CultureInfo.InvariantCulture)} Hz" : Unavailable)}";
        var size = display.DiagonalInches is { } inches ? t["display.inches", inches.ToString("0.#", CultureInfo.InvariantCulture)] : t["display.sizeUnavailable"];
        var primary = display.IsPrimary ? t["display.primary"] : "";
        return $"{name} | {mode} | {size} | {DisplayNames.Connection(t, display.Connection)}{primary} | {t["display.connectedGpu", Text(display.ConnectedGpu)]}";
    }

    private void WriteSensors(IReadOnlyList<SensorReading> sensors)
    {
        var shown = sensors
            .Where(sensor => sensor.SensorType is "Temperature" or "Fan" && sensor.Status != SensorStatus.NoValue)
            .OrderBy(sensor => sensor.HardwareName)
            .ThenBy(sensor => sensor.SensorType)
            .ToList();

        WriteSection(t["section.sensors", sensors.Count], shown, sensor =>
            $"{sensor.HardwareName} / {sensor.SensorName}: {(sensor.Status == SensorStatus.Ok ? $"{sensor.Value!.Value.ToString("0.#", CultureInfo.InvariantCulture)} {sensor.Unit}" : t["sensor.invalid"])}");
    }

    private void WriteTestRun(TestRunResult run)
    {
        output.WriteLine();
        output.WriteLine($"[{t["test.heading"]}] {EnumText.Overall(t, run.Overall)}");
        foreach (var phase in run.Phases)
        {
            var loadText = phase.AverageLoadPercent is { } load
                ? load.ToString("0", CultureInfo.InvariantCulture) + " %"
                : Unavailable;
            output.WriteLine(FormattableString.Invariant($"  {EnumText.Phase(t, phase.Phase)}: {EnumText.Status(t, phase.Status)} | {phase.ActualSeconds:0}/{phase.PlannedSeconds:0} s | {loadText}"));
        }

        foreach (var item in run.Statistics.Where(item => item.SensorType == "Temperature" && item.Average is not null && item.Phase != TestPhase.Finished))
        {
            var statistics = t[
                "test.statistics",
                item.Minimum?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
                item.Maximum?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
                item.Average?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
                item.ValidSamples];
            output.WriteLine(FormattableString.Invariant($"  {EnumText.Phase(t, item.Phase)} {item.HardwareName} / {item.SensorName}: {statistics}"));
        }

        foreach (var anomaly in run.Anomalies)
        {
            output.WriteLine($"  ! {EnumText.Phase(t, anomaly.Phase)}: {EnumText.Anomaly(t, anomaly.Code)}{(anomaly.Detail is null ? "" : $" ({anomaly.Detail})")}");
        }
    }

    private void WriteCooling(CoolingEstimate cooling)
    {
        var lines = new List<string> { t["cooling.summary", CoolingText(cooling.Outcome), Rpm(cooling.PumpRpm), Rpm(cooling.CpuFanRpm)] };
        lines.AddRange(cooling.Notes.Select(note => t["cooling.note", NoteText(note, cooling)]));
        WriteSection(t["cooling.heading"], lines, line => line);
    }

    private void WriteSection<T>(string title, IReadOnlyList<T> items, Func<T, string> format)
    {
        output.WriteLine();
        output.WriteLine($"[{title}]");
        if (items.Count == 0)
        {
            output.WriteLine($"  {Unavailable}");
        }

        foreach (var item in items)
        {
            output.WriteLine($"  {format(item)}");
        }
    }

    private string CoolingText(CoolingOutcome outcome) => outcome switch
    {
        CoolingOutcome.LikelyLiquid => t["cooling.likelyLiquid"],
        CoolingOutcome.LikelyAir => t["cooling.likelyAir"],
        CoolingOutcome.AbnormalWiring => t["cooling.abnormalWiring"],
        _ => t["cooling.noData"],
    };

    private string ActivationText(WindowsActivation activation) => activation switch
    {
        WindowsActivation.Activated => t["activation.activated"],
        WindowsActivation.GracePeriod => t["activation.gracePeriod"],
        WindowsActivation.Notification => t["activation.notification"],
        WindowsActivation.NotActivated => t["activation.notActivated"],
        _ => Unavailable,
    };

    private string NoteText(CoolingNote note, CoolingEstimate cooling) => note switch
    {
        CoolingNote.PumpHeaderIsNotProof => t["cooling.note.pumpHeaderIsNotProof"],
        CoolingNote.PumpMayBeUnreported => t["cooling.note.pumpMayBeUnreported"],
        CoolingNote.CheckCoolerWiring => t["cooling.note.checkCoolerWiring"],
        CoolingNote.PassiveCoolingPossible => t["cooling.note.passiveCoolingPossible"],
        CoolingNote.UnlabeledFansPresent => t["cooling.note.unlabeledFansPresent", cooling.UnlabeledRunningFans],
        CoolingNote.SensorAccessLimited => t["cooling.note.sensorAccessLimited"],
        _ => note.ToString(),
    };

    private string YesNo(bool value) => value ? t["common.yes"] : t["common.no"];

    private string Text(string? value) => value ?? Unavailable;

    private string Gib(ulong? bytes) => bytes is { } value
        ? FormattableString.Invariant($"{value / BYTES_PER_GIB:0.#} GB")
        : Unavailable;

    private string Gb(ulong? bytes) => bytes is { } value
        ? FormattableString.Invariant($"{value / BYTES_PER_GB:0} GB")
        : Unavailable;

    private string Mts(int? speed) => speed is { } value ? FormattableString.Invariant($"{value} MT/s") : Unavailable;

    private string Rpm(double? rpm) => rpm is { } value ? FormattableString.Invariant($"{value:0} RPM") : t["cooling.noRpm"];

    private string Resolution(DisplayInfo display) =>
        display.WidthPixels is { } width && display.HeightPixels is { } height
            ? FormattableString.Invariant($"{width}x{height}")
            : Unavailable;
}
