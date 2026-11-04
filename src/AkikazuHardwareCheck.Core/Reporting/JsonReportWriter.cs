using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Reporting;

public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Temperature and load rows go in ready-made, so the website's import needs no sensor-naming rules of its own.</summary>
    public static string Serialize(CheckReport report) =>
        JsonSerializer.Serialize(report with
        {
            Temperatures = report.TestRun is { } run ? TemperatureSummary.Build(run, null) : null,
            LoadMetrics = report.TestRun is { } loaded ? LoadMetricSummary.Build(loaded) : null,
        }, Options);

    /// <summary>Returns the failure reason, or null on success. A bad output path must not discard the shown results.</summary>
    public static string? TrySave(CheckReport report, string path)
    {
        try
        {
            File.WriteAllText(path, Serialize(report), new UTF8Encoding(false));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return exception.Message;
        }
    }
}
