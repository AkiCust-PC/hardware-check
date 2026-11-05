using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Reporting;

/// <summary>The CPU details window. Only values that could be read appear.</summary>
public static class CpuSummary
{
    private const double KILOBYTES_PER_MEGABYTE = 1024;

    public static List<SummaryLine> Details(Translator t, HardwareInventory hardware)
    {
        var lines = new List<SummaryLine>();

        void Add(string key, string? value)
        {
            if (value is not null)
            {
                lines.Add(new SummaryLine(t[key], value));
            }
        }

        foreach (var cpu in hardware.Cpus)
        {
            Add("summary.processor", cpu.Model is { } model ? DisplayNames.CleanModel(model) : null);
            Add("cpu.manufacturer", cpu.Manufacturer);
            Add("cpu.socket", cpu.Socket);
            Add("cpu.coresThreads", cpu is { PhysicalCores: { } cores, LogicalThreads: { } threads } ? t["cpu.coresThreadsValue", cores, threads] : null);
            Add("cpu.baseClock", cpu.MaxClockMhz is { } clock and > 0 ? $"{clock.ToString(CultureInfo.InvariantCulture)} MHz" : null);
            Add("cpu.cacheL2", CacheText(cpu.L2CacheKilobytes));
            Add("cpu.cacheL3", CacheText(cpu.L3CacheKilobytes));
            Add("cpu.identifier", cpu.Identifier);
            Add("cpu.virtualization", cpu.IsVirtualizationEnabled switch
            {
                true => t["summary.enabled"],
                false => t["summary.disabled"],
                _ => null,
            });
        }

        Add("summary.microcode", hardware.Reliability?.MicrocodeRevision);
        Add("summary.whea", HardwareSummary.WheaText(t, hardware.Reliability));
        return lines;
    }

    private static string? CacheText(int? kilobytes) =>
        kilobytes is { } size and > 0 ? $"{(size / KILOBYTES_PER_MEGABYTE).ToString("0.#", CultureInfo.InvariantCulture)} MB" : null;
}
