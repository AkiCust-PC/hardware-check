using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Reporting;

/// <summary>Formats drive health for the screen and the report.</summary>
public static class DiskHealthSummary
{
    public static bool NeedsAttention(DiskHealth health) => health.Status is DiskHealthStatus.Caution or DiskHealthStatus.Bad;

    /// <summary>One line, for example "Good 99% 46°C On 4361h Written 12.3TB Media errors 0". Reads and cycle counts stay in the details.</summary>
    public static string Headline(Translator t, DiskHealth health)
    {
        string?[] parts =
        [
            t[EnumText.Key("diskHealth", health.Status)],
            health.LifePercent is { } life ? $"{life}%" : null,
            health.TemperatureCelsius is { } temperature ? $"{temperature}°C" : null,
            health.PowerOnHours is { } hours ? t["disk.linePowerOn", hours] : null,
            health.DataWrittenBytes is { } written ? t["disk.lineWritten", DisplayNames.DiskSize(written)] : null,
            health.MediaErrors is { } errors ? t["disk.lineErrors", errors] : null,
        ];
        return string.Join(" ", parts.Where(part => part is not null));
    }

    /// <summary>The details table, in the order CrystalDiskInfo users expect. Only values the drive provided appear.</summary>
    public static List<SummaryLine> Details(Translator t, DiskHealth health)
    {
        var lines = new List<SummaryLine>
        {
            new(t["disk.status"], t[EnumText.Key("diskHealth", health.Status)]),
        };

        void Add(string key, string? value)
        {
            if (value is not null)
            {
                lines.Add(new SummaryLine(t[key], value));
            }
        }

        Add("disk.life", health.LifePercent is { } life ? $"{life}%" : null);
        Add("disk.temperature", health.TemperatureCelsius is { } temperature ? $"{temperature}°C" : null);
        Add("disk.mediaErrors", Count(health.MediaErrors));
        Add("disk.spare", health.AvailableSparePercent is { } spare ? t["disk.spareValue", spare, health.SpareThresholdPercent ?? 0] : null);
        Add("disk.powerOnHours", Count(health.PowerOnHours));
        Add("disk.powerCycles", Count(health.PowerCycles));
        Add("disk.unsafeShutdowns", Count(health.UnsafeShutdowns));
        Add("disk.errorLog", Count(health.ErrorLogEntries));
        Add("disk.dataRead", health.DataReadBytes is { } read ? DisplayNames.DiskSize(read) : null);
        Add("disk.dataWritten", health.DataWrittenBytes is { } written ? DisplayNames.DiskSize(written) : null);
        Add("disk.reallocated", Count(health.ReallocatedSectors));
        Add("disk.pending", Count(health.PendingSectors));
        Add("disk.uncorrectable", Count(health.UncorrectableSectors));

        foreach (var finding in health.Findings ?? [])
        {
            lines.Add(new SummaryLine(t["disk.finding"], t[EnumText.Key("diskFinding", finding)]));
        }

        return lines;
    }

    private static string? Count(ulong? value) => value?.ToString("N0", CultureInfo.InvariantCulture);
}
