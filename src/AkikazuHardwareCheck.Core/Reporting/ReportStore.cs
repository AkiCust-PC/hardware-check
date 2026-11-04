using System.Globalization;
using System.Text;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Reporting;

public sealed record SavedReport(string HtmlPath, string JsonPath);

/// <summary>Saves to the folder staff chose. Files stay until staff delete them: nothing is uploaded or cleaned up automatically.</summary>
public static class ReportStore
{
    public static SavedReport Save(CheckReport report, string folder, Translator t)
    {
        Directory.CreateDirectory(folder);
        var name = FileStem(report);
        var htmlPath = Path.Combine(folder, $"{name}.html");
        var jsonPath = Path.Combine(folder, $"{name}.json");
        File.WriteAllText(htmlPath, new HtmlReportGenerator(t).Generate(report), new UTF8Encoding(false));
        File.WriteAllText(jsonPath, JsonReportWriter.Serialize(report), new UTF8Encoding(false));
        return new SavedReport(htmlPath, jsonPath);
    }

    public static string FileStem(CheckReport report) =>
        $"hardware-check-{report.GeneratedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
}
