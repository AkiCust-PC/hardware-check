using System.Globalization;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Reporting;

public sealed record SummaryLine(string Label, string Value);

/// <summary>The hardware overview shown on screen, copied to the clipboard and printed in the report.</summary>
public static class HardwareSummary
{
    /// <param name="t">The language to format in.</param>
    /// <param name="hardware">The hardware to list.</param>
    /// <param name="isFull">False lists only the main hardware models and parameters; true adds adapters, status, network and system lines.</param>
    public static List<SummaryLine> Lines(Translator t, HardwareInventory hardware, bool isFull = true)
    {
        var unavailable = t["common.unavailable"];
        var lines = new List<SummaryLine>();

        // Anything that could not be read is left out, so every line shown is a real reading.
        void AddAll<T>(string label, IReadOnlyList<T> items, Func<T, string> format) =>
            lines.AddRange(items.Select(item => new SummaryLine(label, format(item))));

        void AddIfRead(string label, string? value)
        {
            if (value is not null)
            {
                lines.Add(new SummaryLine(label, value));
            }
        }

        if (isFull)
        {
            lines.Add(new SummaryLine(t["summary.time"], TimeText(DateTimeOffset.Now, TimeZoneInfo.Local)));
        }

        if (hardware.Computer is { } computer)
        {
            var brand = computer.Manufacturer is { } maker ? DisplayNames.BoardVendor(maker) : null;
            lines.Add(new SummaryLine(t[computer.IsLaptop ? "summary.laptopPc" : "summary.desktopPc"], DisplayNames.ComputerName(brand, computer.Model)));
        }

        var integrated = hardware.Gpus.FirstOrDefault(gpu => gpu.Kind == GpuKind.Integrated);
        var microcode = isFull ? hardware.Reliability?.MicrocodeRevision : null;
        AddAll(t["summary.processor"], hardware.Cpus, cpu => FormatCpu(t, cpu, integrated, unavailable, microcode));
        AddGraphics(t, hardware, lines, unavailable);
        AddIfRead(t["section.motherboard"], hardware.Motherboard is { } board ? FormatBoard(board, unavailable, isFull) : null);
        AddIfRead(t["section.memory"], hardware.Memory is { } memory ? FormatMemory(memory, unavailable) : null);
        AddAll(t["section.disks"], hardware.Disks, disk => isFull ? FormatDiskWithHealth(t, hardware, disk, unavailable) : FormatDisk(disk, unavailable));
        AddAll(t["section.displays"], hardware.Displays, display => FormatDisplay(t, display, unavailable));
        if (isFull)
        {
            AddAll(t["summary.wiredCard"], hardware.WiredAdapters ?? [], name => name);
            AddAll(t["summary.wirelessCard"], hardware.WirelessAdapters ?? [], name => name);
            foreach (var card in hardware.Gpus.Where(gpu => gpu.Kind is not (GpuKind.Software or GpuKind.Integrated)))
            {
                AddIfRead(t["summary.gpuDriver"], FormatDriver(card));
            }

            if (hardware.Battery is { } battery)
            {
                lines.Add(new SummaryLine(t["summary.battery"], t["summary.batteryValue", battery.HealthPercent, DisplayNames.WattHours(battery.FullMilliwattHours), DisplayNames.WattHours(battery.DesignedMilliwattHours)]));
            }

            if (hardware.SystemDrive is { } drive)
            {
                lines.Add(new SummaryLine(t["summary.systemDrive"], t["summary.systemDriveValue", DisplayNames.DiskSize(drive.UsedBytes), DisplayNames.DiskSize(drive.TotalBytes)]));
            }

            AddIfRead(t["summary.network"], hardware.Network is { } network ? FormatNetwork(t, network) : null);
            AddIfRead(t["summary.system"], FormatSystem(t, hardware.Environment));
            if (hardware.Locale is { } locale)
            {
                lines.Add(new SummaryLine(t["summary.languageEnvironment"], $"{FormatLocale(t, locale)} · {DisplayNames.EncodingName(locale.AnsiCodePage)}"));
            }

            AddIfRead(t["summary.secureBoot"], FormatSecurity(t, hardware.Security));
        }

        return Tidy(lines, unavailable);
    }

    /// <summary>Removes the words for anything unread, and gives devices of one kind a single label with one line each.</summary>
    private static List<SummaryLine> Tidy(List<SummaryLine> lines, string unavailable)
    {
        var tidy = new List<SummaryLine>();
        foreach (var line in lines)
        {
            var value = string.Join(Environment.NewLine, line.Value.Split(Environment.NewLine).Select(part => string.Join(" ", part.Replace(unavailable, "", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries))).Where(part => part.Length > 0));
            if (value.Length == 0)
            {
                continue;
            }

            if (tidy.Count > 0 && tidy[^1].Label == line.Label)
            {
                tidy[^1] = tidy[^1] with { Value = tidy[^1].Value + Environment.NewLine + value };
            }
            else
            {
                tidy.Add(line with { Value = value });
            }
        }

        return tidy;
    }

    /// <summary>A drive and its health stay together, so two drives can always be told apart.</summary>
    private static string FormatDiskWithHealth(Translator t, HardwareInventory hardware, DiskInfo disk, string unavailable)
    {
        var name = FormatDisk(disk, unavailable);
        var health = hardware.DiskHealth?.FirstOrDefault(item => item.DeviceId == disk.DeviceId && item.Status != DiskHealthStatus.Unknown);
        return health is null ? name : name + Environment.NewLine + DiskHealthSummary.Headline(t, health);
    }

    /// <summary>When Windows was installed and last started, shown at the very end of the full list.</summary>
    public static List<SummaryLine> HistoryLines(Translator t, HardwareInventory hardware)
    {
        var parts = new List<string>();
        if (hardware.Environment.OperatingSystem?.InstallDate is { } installed)
        {
            parts.Add($"{t["summary.installDate"]} {installed}");
        }

        if (hardware.Reliability?.LastBootAt is { } bootedAt)
        {
            var hours = (long)(DateTimeOffset.Now - bootedAt).TotalHours;
            parts.Add($"{t["summary.lastBoot"]} {t["summary.lastBootValue", bootedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), hours]}");
        }

        return parts.Count == 0 ? [] : [new SummaryLine(t["summary.systemRecord"], string.Join(" · ", parts))];
    }

    /// <summary>Windows hardware errors over the recent window, or null when the log could not be read.</summary>
    public static string? WheaText(Translator t, ReliabilityInfo? reliability) =>
        reliability?.WheaErrors is { } count ? t["summary.wheaValue", ReliabilityCollector.WHEA_WINDOW_DAYS, count] : null;

    public static string TimeText(DateTimeOffset now, TimeZoneInfo zone) =>
        $"{now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {zone.DisplayName}";

    /// <summary>One row per kind of device, with each device on its own line. A kind with no device has no row.</summary>
    public static List<SummaryLine> PeripheralLines(Translator t, IReadOnlyList<Peripheral>? peripherals)
    {
        var all = peripherals ?? [];
        var lines = new List<SummaryLine>();
        foreach (var kind in Enum.GetValues<PeripheralKind>())
        {
            var names = all.Where(item => item.Kind == kind).Select(item =>
            {
                var name = item.Name ?? t["peripheral.unnamed"];
                return item.IsBluetooth ? t["peripheral.viaBluetooth", name] : name;
            }).ToList();
            if (names.Count > 0)
            {
                lines.Add(new SummaryLine(t[EnumText.Key("peripheral", kind)], string.Join(Environment.NewLine, names)));
            }
        }

        return lines;
    }

    public static string AsText(IEnumerable<SummaryLine> lines) =>
        string.Join(Environment.NewLine, lines.Select(line => $"{line.Label}: {line.Value}"));

    /// <summary>The three settings usually match, and a single name then says it all.</summary>
    private static string FormatLocale(Translator t, LocaleInfo locale) =>
        locale.DisplayLanguage == locale.Format && locale.Format == locale.SystemLocale
            ? LanguageName(locale.DisplayLanguage)
            : t["summary.localeValue", LanguageName(locale.DisplayLanguage), LanguageName(locale.Format), LanguageName(locale.SystemLocale)];

    /// <summary>The name in that language itself, so it reads correctly whatever language the tool is showing.</summary>
    private static string LanguageName(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    private static string FormatCpu(Translator t, CpuInfo cpu, GpuInfo? integrated, string unavailable, string? microcode)
    {
        var text = FormatCpuName(t, cpu, integrated, unavailable);
        return microcode is null ? text : $"{text} {t["summary.microcodeShort", microcode]}";
    }

    private static string FormatCpuName(Translator t, CpuInfo cpu, GpuInfo? integrated, string unavailable)
    {
        var name = cpu.Model is { } model ? DisplayNames.CleanModel(model) : unavailable;
        var text = cpu is { PhysicalCores: { } cores, LogicalThreads: { } threads } ? t["summary.cpu", name, cores, threads] : name;
        if (integrated is null)
        {
            return text;
        }

        // A specific name such as "Radeon 780M" is worth keeping; "Intel Graphics" says no more than the label does.
        return DisplayNames.IsGenericIntegratedGpu(integrated.Model, integrated.Vendor)
            ? $"{text} {t["summary.withIntegrated"]}"
            : $"{text} {t["summary.withIntegrated"]} {DisplayNames.GpuModel(integrated.Model!)}";
    }

    private static void AddGraphics(Translator t, HardwareInventory hardware, List<SummaryLine> lines, string unavailable)
    {
        var label = t["summary.graphics"];
        var cards = hardware.Gpus.Where(gpu => gpu.Kind is not (GpuKind.Software or GpuKind.Integrated)).ToList();
        if (cards.Count == 0)
        {
            lines.Add(new SummaryLine(label, hardware.Gpus.Count == 0 ? unavailable : t["summary.noDiscreteGpu"]));
            return;
        }

        lines.AddRange(cards.Select(gpu => new SummaryLine(label, FormatGpu(t, gpu, unavailable))));
    }

    /// <summary>Publisher, version and date. Null when the driver could not be read at all.</summary>
    private static string? FormatDriver(GpuInfo gpu)
    {
        string?[] parts = [gpu.DriverProvider, gpu.DriverVersion, gpu.DriverDate];
        var known = parts.Where(part => part is not null).ToList();
        return known.Count > 0 ? string.Join(" ", known) : null;
    }

    /// <summary>The activation state follows the name. A state that could not be read adds nothing rather than a guess.</summary>
    private static string? FormatSystem(Translator t, ProbeEnvironment environment)
    {
        if (environment.OperatingSystem is not { Name: { } name } system)
        {
            return null;
        }

        var text = $"{name} {system.DisplayVersion}".TrimEnd();
        return environment.Activation == WindowsActivation.Unknown ? text : $"{text} {t[EnumText.Key("activation", environment.Activation)]}";
    }

    /// <summary>Secure Boot and TPM are both Windows 11 security requirements, so they share a row.</summary>
    private static string? FormatSecurity(Translator t, SecurityInfo? security)
    {
        string?[] parts =
        [
            security?.SecureBootEnabled switch
            {
                true => t["summary.enabled"],
                false => t["summary.disabled"],
                _ => null,
            },
            security?.TpmVersion is { } version ? $"TPM {version}" : null,
        ];
        var known = parts.Where(part => part is not null).ToList();
        return known.Count > 0 ? string.Join(" ", known) : null;
    }

    private static string FormatGpu(Translator t, GpuInfo gpu, string unavailable)
    {
        var model = gpu.Model is { } name ? DisplayNames.GpuModel(name) : unavailable;
        return gpu.DedicatedMemoryBytes is { } bytes and > 0 ? t["summary.gpuMemory", model, DisplayNames.MemorySize(bytes)] : model;
    }

    private static string FormatBoard(MotherboardInfo board, string unavailable, bool isFull)
    {
        var vendor = board.Manufacturer is { } manufacturer ? DisplayNames.BoardVendor(manufacturer) : unavailable;
        var bios = isFull && board.BiosVersion is { } version ? $" BIOS {version}{(board.BiosDate is { } date ? $" {date}" : "")}" : "";
        return $"{vendor} {board.Model ?? unavailable}{bios}";
    }

    /// <summary>Brand, type, capacity and layout, speed, then part number: the order memory is sold under.</summary>
    private static string FormatMemory(MemoryInfo memory, string unavailable)
    {
        var total = memory.InstalledBytes ?? memory.SystemVisibleBytes;
        var totalText = total is { } bytes ? DisplayNames.MemorySize(bytes) : unavailable;
        if (memory.Modules.Count == 0)
        {
            return totalText;
        }

        var first = memory.Modules[0];
        var type = memory.Modules.Select(module => module.MemoryType).FirstOrDefault(value => value is not null);
        var speed = memory.Modules.Select(module => module.ConfiguredSpeedMts).FirstOrDefault(value => value is not null);
        var sizes = memory.Modules.Select(module => module.CapacityBytes is { } size ? DisplayNames.MemorySize(size) : unavailable).ToList();
        var layout = sizes.Distinct().Count() == 1
            ? $"{sizes[0]} × {sizes.Count.ToString(CultureInfo.InvariantCulture)}"
            : string.Join(" + ", sizes);
        string?[] parts =
        [
            DisplayNames.MemoryBrand(first.Manufacturer),
            type,
            $"{totalText} ({layout})",
            speed is { } value ? $"{value}MT/s" : null,
            first.PartNumber,
        ];
        return string.Join(" ", parts.Where(part => part is not null));
    }

    /// <summary>Brand, model and capacity first, then how the drive connects.</summary>
    private static string FormatDisk(DiskInfo disk, string unavailable)
    {
        string?[] parts =
        [
            DisplayNames.DiskBrand(disk.Model),
            disk.Model ?? unavailable,
            disk.SizeBytes is { } bytes ? DisplayNames.DiskSize(bytes) : null,
            disk.BusType,
            disk.MediaType,
            disk.PcieGeneration is { } generation ? DisplayNames.PcieLink(generation, disk.PcieLanes) : null,
        ];
        return string.Join(" ", parts.Where(part => part is not null));
    }

    private static string FormatDisplay(Translator t, DisplayInfo display, string unavailable)
    {
        string?[] parts =
        [
            display.Name ?? unavailable,
            display.WidthPixels is { } width && display.HeightPixels is { } height ? $"{width} × {height}" : null,
            display.RefreshRateHz is { } rate ? $"{Math.Round(rate).ToString(CultureInfo.InvariantCulture)}Hz" : null,
            display.DiagonalInches is { } inches ? t["summary.inches", inches.ToString("0.#", CultureInfo.InvariantCulture)] : null,
            DisplayNames.Connection(t, display.Connection),
        ];
        return string.Join(" ", parts.Where(part => part is not null));
    }

    private static string FormatNetwork(Translator t, NetworkConnection network)
    {
        var kind = t[EnumText.Key("network", network.Kind)];
        string?[] parts =
        [
            kind,
            network.LinkMegabits is { } speed ? DisplayNames.LinkSpeed(speed) : null,
            network.SignalPercent is { } signal ? t["summary.signal", signal] : null,
        ];
        return string.Join(" ", parts.Where(part => part is not null));
    }
}
