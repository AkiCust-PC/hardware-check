using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Reporting;

/// <summary>Builds the one-page A4 test report. Every value passes through HTML encoding on its way in.</summary>
public sealed class HtmlReportGenerator(Translator t)
{
    private static readonly string Template = ReadResource("report.ReportTemplate.html");
    private static readonly string Style = ReadResource("report.ReportStyle.css");

    public string Generate(CheckReport report)
    {
        var values = new Dictionary<string, string>
        {
            ["lang"] = Encode(t.Language),
            ["style"] = Style,
            ["title"] = Encode(t["report.title"]),
            ["company"] = Encode(t["report.company"]),
            ["companyContact"] = Encode(t["report.companyContact"]),
            ["hardwareHeading"] = Encode(t["report.hardware"]),
            ["hardwareRows"] = HardwareRows(report.Hardware) + (report.Cooling.Outcome == CoolingOutcome.NoData ? "" : Row(Encode(t["report.cooling"]), Encode(CoolingText(report)))),
            ["temperatureHeading"] = Encode(t["report.temperature"]),
            ["temperatureTable"] = TemperatureTable(report),
            ["testHeading"] = Encode(t["report.testSummary"]),
            ["testRows"] = TestRows(report.TestRun) + ToolRows(report),
            ["conclusion"] = Encode(Conclusion(report)),
            ["disclaimer"] = Encode(t["report.disclaimer"]),
            ["footer"] = Encode(t["report.footer", report.ToolVersion, report.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)]),
        };

        return values.Aggregate(Template, (html, entry) => html.Replace($"{{{{{entry.Key}}}}}", entry.Value, StringComparison.Ordinal));
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Row(params string[] encodedCells) =>
        $"<tr>{string.Concat(encodedCells.Select(cell => $"<td>{cell}</td>"))}</tr>";

    private string HardwareRows(HardwareInventory hardware) =>
        string.Concat(HardwareSummary.Lines(t, hardware).Concat(HardwareSummary.HistoryLines(t, hardware)).Select(line => Row(Encode(line.Label), Encode(line.Value))));

    private string CoolingText(CheckReport report)
    {
        var estimate = EnumText.CoolingEstimate(t, report.Cooling.Outcome);
        return report.Cooling.CoolerName is { } name ? $"{name} {estimate}" : estimate;
    }

    private string TemperatureTable(CheckReport report)
    {
        var header = $"<tr><th>{Encode(t["report.device"])}</th><th>{Encode(t["report.condition"])}</th><th>{Encode(t["report.minimum"])}</th><th>{Encode(t["report.maximum"])}</th><th>{Encode(t["report.average"])}</th></tr>";
        if (report.TestRun is not { } run)
        {
            return $"<table class=\"data\">{header}<tr><td colspan=\"5\">{Encode(t["status.notRun"])}</td></tr></table>";
        }

        var target = report.Hardware.Gpus.FirstOrDefault(gpu => gpu.Model == run.Phases.FirstOrDefault(phase => phase.TargetDevice is not null)?.TargetDevice);
        var rows = TemperatureSummary.Build(run, target).Where(row => row.State != TemperatureRowState.Unavailable).Select(row =>
        {
            var device = row.IsCpu ? "CPU" : "GPU";
            var condition = EnumText.Key("report.condition", row.Phase);
            return row.State == TemperatureRowState.Measured
                ? Row(device, Encode(t[condition]), Celsius(row.Minimum), Celsius(row.Maximum), Celsius(row.Average))
                : $"<tr><td>{device}</td><td>{Encode(t[condition])}</td><td colspan=\"3\" class=\"muted\">{Encode(StateText(row.State))}</td></tr>";
        });
        return $"<table class=\"data\">{header}{string.Concat(rows)}</table>";
    }

    private string StateText(TemperatureRowState state) => state switch
    {
        TemperatureRowState.InsufficientData => t["status.insufficientData"],
        _ => t["status.notRun"],
    };

    private static string Celsius(double? value) => value is { } number ? $"{number.ToString("0.#", CultureInfo.InvariantCulture)} °C" : "";

    private string TestRows(TestRunResult? run)
    {
        if (run is null)
        {
            return Row(Encode(t["test.heading"]), Encode(t["status.notRun"]));
        }

        var phases = run.Phases.Where(phase => phase.IsRequested).Select(phase => Row(Encode(EnumText.Phase(t, phase.Phase)), Encode(EnumText.Status(t, phase.Status))));
        var metrics = LoadMetricSummary.Build(run).Select(row => Row(Encode(t[EnumText.Key("metric", row.Metric)]), Encode(LoadMetricSummary.Text(t, row))));
        var anomalies = run.Anomalies.Select(anomaly => Row(Encode(EnumText.Phase(t, anomaly.Phase)), $"<span class=\"alert\">{Encode(EnumText.Anomaly(t, anomaly.Code))}</span>"));
        return string.Concat(phases.Concat(metrics).Concat(anomalies));
    }

    private string Conclusion(CheckReport report) => HasProblem(report) ? t["report.conclusion.needsReview"] : report.TestRun?.Overall switch
    {
        OverallResult.NoObviousIssue => t["report.conclusion.noObviousIssue"],
        OverallResult.NeedsReview => t["report.conclusion.needsReview"],
        OverallResult.Incomplete => t["report.conclusion.incomplete"],
        _ => t["report.conclusion.notRun"],
    };

    private static bool HasProblem(CheckReport report) =>
        (report.Hardware.DiskHealth ?? []).Any(DiskHealthSummary.NeedsAttention)
        || ToolTestSummary.HasProblem(report.MemoryTest)
        || ToolTestSummary.HasProblem(report.DiskTest)
        || report.AudioCheck is { SpeakerHeard: false } or { MicrophoneHeard: false };

    private string ToolRows(CheckReport report)
    {
        // Hardware errors are evidence about stability, so they sit with the test results and are read after the tests.
        var rows = HardwareSummary.WheaText(t, report.Hardware.Reliability) is { } whea
            ? Row(Encode(t["summary.whea"]), Marked(whea, report.Hardware.Reliability?.WheaErrors > 0))
            : "";
        if (report.MemoryTest is { } memory)
        {
            rows += Row(Encode(t["ui.memoryTest"]), Marked(ToolTestSummary.Text(t, memory), ToolTestSummary.HasProblem(memory)));
        }

        if (report.DiskTest is { } disk)
        {
            rows += Row(Encode(t["ui.diskTest"]), Marked(ToolTestSummary.Text(t, disk), ToolTestSummary.HasProblem(disk)));
        }

        if (report.AudioCheck?.SpeakerHeard is { } speaker)
        {
            rows += Row(Encode(t["ui.speakerTest"]), Marked(t[speaker ? "audioCheck.heard" : "audioCheck.notHeard"], !speaker));
        }

        if (report.AudioCheck?.MicrophoneHeard is { } microphone)
        {
            rows += Row(Encode(t["ui.micTest"]), Marked(t[microphone ? "audioCheck.heard" : "audioCheck.notHeard"], !microphone));
        }

        return rows;
    }

    private static string Marked(string text, bool isAlert) =>
        isAlert ? $"<span class=\"alert\">{Encode(text)}</span>" : Encode(text);

}
