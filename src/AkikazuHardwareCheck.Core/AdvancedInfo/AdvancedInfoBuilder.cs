using System.Globalization;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Core.AdvancedInfo;

/// <summary>
/// Builds the advanced view from the inventory the main window already read. It performs no hardware access, so
/// opening the window cannot enumerate a second set of devices or open a second sensor source.
/// </summary>
public static class AdvancedInfoBuilder
{
    private const double KILOBYTES_PER_MEGABYTE = 1024;

    public static AdvancedSnapshot Build(Translator t, HardwareInventory hardware, IReadOnlyList<SensorReading> sensors, DateTimeOffset capturedAt) =>
        new(capturedAt, CheckReport.CurrentToolVersion,
        [
            Overview(t, hardware, capturedAt),
            Cpu(t, hardware, sensors),
            Graphics(t, hardware, sensors),
            Motherboard(t, hardware, sensors),
            Memory(t, hardware),
            Storage(t, hardware, sensors),
            Displays(t, hardware),
            Network(t, hardware),
            Peripherals(t, hardware),
            CoolingAndPower(t, hardware, sensors),
            Windows(t, hardware),
            SoftwareAndDrivers(t, hardware),
            EventLog(t, hardware),
            Other(t, hardware),
        ]);

    private static InfoNode Overview(Translator t, HardwareInventory hardware, DateTimeOffset capturedAt)
    {
        var environment = hardware.Environment;
        var fields = new List<InfoField>
        {
            Field("overview.time", t["summary.time"], capturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), provenance: new InfoProvenance("This tool", "System clock")),
        };

        if (hardware.Computer is { } computer)
        {
            var brand = computer.Manufacturer is { } maker ? DisplayNames.BoardVendor(maker) : null;
            var label = computer.IsLaptop ? t["summary.laptopPc"] : t["summary.desktopPc"];
            fields.Add(Field("overview.computer", label, DisplayNames.ComputerName(brand, computer.Model), InfoOrigin.Firmware));
        }

        if (environment.OperatingSystem is { } os)
        {
            fields.Add(Field("overview.os", t["summary.system"], Join(os.Name, os.DisplayVersion), InfoOrigin.Windows));
            fields.Add(Field("overview.build", t["advanced.build"], os.Build));
            fields.Add(Field("overview.architecture", t["advanced.architecture"], os.Architecture));
            fields.Add(Field("overview.installDate", t["summary.installDate"], os.InstallDate));
        }
        else
        {
            fields.Add(Missing("overview.os", t["summary.system"], InfoState.NotCollected));
        }

        fields.Add(Field("overview.activation", t["advanced.activation"], t[EnumText.Key("activation", environment.Activation)]));
        fields.Add(Field("overview.admin", t["advanced.access"], AccessText(t, environment)));
        fields.Add(Field("overview.whea", t["summary.whea"], HardwareSummary.WheaText(t, hardware.Reliability), InfoOrigin.Windows));
        if (hardware.Reliability?.LastBootAt is { } bootedAt)
        {
            fields.Add(Field("overview.lastBoot", t["summary.lastBoot"], bootedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), InfoOrigin.Windows));
        }

        return Node("overview", t["advanced.overview"], [Group("overview", t["advanced.overview"], fields)]);
    }

    private static InfoNode Cpu(Translator t, HardwareInventory hardware, IReadOnlyList<SensorReading> sensors)
    {
        var fields = new List<InfoField>();
        if (hardware.Cpus.Count == 0)
        {
            fields.Add(Missing("cpu", t["section.cpu"], InfoState.Unsupported));
        }

        for (var index = 0; index < hardware.Cpus.Count; index++)
        {
            var cpu = hardware.Cpus[index];
            var suffix = hardware.Cpus.Count > 1 ? $" {index + 1}" : "";
            fields.Add(Field($"cpu.{index}.model", t["summary.processor"] + suffix, cpu.Model is { } model ? DisplayNames.CleanModel(model) : null, InfoOrigin.Firmware, raw: cpu.Model));
            fields.Add(Field($"cpu.{index}.manufacturer", t["cpu.manufacturer"], cpu.Manufacturer, InfoOrigin.Firmware));
            fields.Add(Field($"cpu.{index}.socket", t["cpu.socket"], cpu.Socket, InfoOrigin.Firmware));
            fields.Add(Field($"cpu.{index}.cores", t["cpu.coresThreads"], cpu is { PhysicalCores: { } cores, LogicalThreads: { } threads } ? t["cpu.coresThreadsValue", cores, threads] : null, InfoOrigin.Firmware));
            fields.Add(Field($"cpu.{index}.maxClock", t["advanced.maxClock"], Value(cpu.MaxClockMhz, "0", "MHz"), InfoOrigin.Firmware, "MHz", Raw(cpu.MaxClockMhz)));
            fields.Add(Field($"cpu.{index}.l2", t["cpu.cacheL2"], CacheText(cpu.L2CacheKilobytes), InfoOrigin.Firmware));
            fields.Add(Field($"cpu.{index}.l3", t["cpu.cacheL3"], CacheText(cpu.L3CacheKilobytes), InfoOrigin.Firmware));
            fields.Add(Field($"cpu.{index}.identifier", t["cpu.identifier"], cpu.Identifier, InfoOrigin.Firmware));
            fields.Add(Field($"cpu.{index}.virtualization", t["cpu.virtualization"], cpu.IsVirtualizationEnabled switch
            {
                true => t["summary.enabled"],
                false => t["summary.disabled"],
                _ => null,
            }, InfoOrigin.Firmware));
        }

        if (hardware.Reliability is { } reliability)
        {
            fields.Add(Field("cpu.microcode", t["summary.microcode"], reliability.MicrocodeRevision, InfoOrigin.Firmware, provenance: new InfoProvenance("Windows", "Registry")));
            fields.Add(Field("cpu.whea", t["summary.whea"], HardwareSummary.WheaText(t, reliability), InfoOrigin.Windows, provenance: new InfoProvenance("Windows", "wevtapi")));
        }

        fields.Add(Missing("cpu.cpuid", t["advanced.cpuid"], InfoState.NotCollected));

        var groups = new List<InfoGroup> { Group("cpu", t["advanced.basic"], fields) };
        AddSensorGroups(groups, t, hardware, sensors, reading => SensorSelector.IsCpu(reading.HardwareType), "cpu.sensors");
        return Node("cpu", t["section.cpu"], groups);
    }

    private static InfoNode Graphics(Translator t, HardwareInventory hardware, IReadOnlyList<SensorReading> sensors)
    {
        var gpus = hardware.Gpus.Where(gpu => gpu.Kind != GpuKind.Software).ToList();
        if (gpus.Count == 0)
        {
            return Node("gpu", t["section.gpu"], [Group("gpu", t["section.gpu"], [Missing("gpu", t["section.gpu"], InfoState.Unsupported)])]);
        }

        var groups = new List<InfoGroup>();
        for (var index = 0; index < gpus.Count; index++)
        {
            var gpu = gpus[index];
            var suffix = gpus.Count > 1 ? $" {index + 1}" : "";
            var provenance = new InfoProvenance("DXGI/D3D11", "IDXGIAdapter1", IsRefreshable: false);
            var fields = new List<InfoField>
            {
                Field($"gpu.{index}.model", t["summary.graphics"] + suffix, gpu.Model is { } model ? DisplayNames.GpuModel(model) : null, InfoOrigin.Firmware, raw: gpu.Model, provenance: provenance),
                Field($"gpu.{index}.vendor", t["cpu.manufacturer"], gpu.Vendor, InfoOrigin.Firmware, provenance: provenance),
                Field($"gpu.{index}.kind", t["advanced.kind"], t[EnumText.Key("gpu.kind", gpu.Kind)], InfoOrigin.Windows, provenance: provenance),
                Field($"gpu.{index}.memory", t["advanced.vram"], gpu.DedicatedMemoryBytes is { } bytes ? DisplayNames.MemorySize(bytes) : null, InfoOrigin.Firmware, "GB", Raw(gpu.DedicatedMemoryBytes), provenance: provenance),
                Field($"gpu.{index}.driver", t["summary.gpuDriver"], DriverText(gpu), InfoOrigin.Driver),
                Field($"gpu.{index}.luid", t["advanced.luid"], gpu.AdapterLuid, InfoOrigin.Windows, provenance: provenance),
                Field($"gpu.{index}.boardModel", t["advanced.boardModel"], null, InfoOrigin.Inferred, note: t["advanced.boardModelNote"]),
                Missing($"gpu.{index}.pcie", t["advanced.pcieLink"], InfoState.NotCollected),
            };
            groups.Add(Group($"gpu.{index}", t["section.gpu"] + suffix, fields));
            AddSensorGroups(groups, t, hardware, sensors, reading => SensorSelector.IsGpu(reading.HardwareType) && SensorSelector.MatchesGpu(reading.HardwareType, reading.HardwareName, gpu.Model), $"gpu.{index}.sensors");
        }

        return Node("gpu", t["section.gpu"], groups);
    }

    private static InfoNode Motherboard(Translator t, HardwareInventory hardware, IReadOnlyList<SensorReading> sensors)
    {
        if (hardware.Motherboard is not { } board)
        {
            return Node("motherboard", t["section.motherboard"], [Group("motherboard", t["section.motherboard"], [Missing("motherboard", t["section.motherboard"], InfoState.Unsupported)])]);
        }

        var fields = new List<InfoField>
        {
            Field("motherboard.manufacturer", t["cpu.manufacturer"], board.Manufacturer is { } maker ? DisplayNames.BoardVendor(maker) : null, InfoOrigin.Firmware),
            Field("motherboard.model", t["section.motherboard"], board.Model, InfoOrigin.Firmware),
            Field("motherboard.bios", t["advanced.biosVersion"], board.BiosVersion, InfoOrigin.Firmware),
            Field("motherboard.biosDate", t["advanced.biosDate"], board.BiosDate, InfoOrigin.Firmware),
            Missing("motherboard.smbios", t["advanced.smbios"], InfoState.NotCollected),
            Missing("motherboard.chipset", t["advanced.chipset"], InfoState.NotCollected),
        };

        var groups = new List<InfoGroup> { Group("motherboard", t["section.motherboard"], fields) };
        AddSensorGroups(groups, t, hardware, sensors, reading => reading.HardwareType is "Motherboard" or "SuperIO" or "EmbeddedController" or "Cooler", "motherboard.sensors");
        return Node("motherboard", t["section.motherboard"], groups);
    }

    private static InfoNode Memory(Translator t, HardwareInventory hardware)
    {
        if (hardware.Memory is not { } memory)
        {
            return Node("memory", t["section.memory"], [Group("memory", t["section.memory"], [Missing("memory", t["section.memory"], InfoState.Unsupported)])]);
        }

        var fields = new List<InfoField>
        {
            Field("memory.total", t["advanced.totalMemory"], memory.InstalledBytes is { } installed ? DisplayNames.MemorySize(installed) : null, InfoOrigin.Firmware, "GB", Raw(memory.InstalledBytes)),
            Field("memory.visible", t["advanced.systemVisible"], memory.SystemVisibleBytes is { } visible ? DisplayNames.MemorySize(visible) : null, InfoOrigin.Windows, "GB", Raw(memory.SystemVisibleBytes)),
        };

        var groups = new List<InfoGroup> { Group("memory", t["section.memory"], fields) };
        for (var index = 0; index < memory.Modules.Count; index++)
        {
            var module = memory.Modules[index];
            var fieldsForModule = new List<InfoField>
            {
                Field($"memory.{index}.capacity", t["advanced.capacity"], module.CapacityBytes is { } capacity ? DisplayNames.MemorySize(capacity) : null, InfoOrigin.Firmware, "GB", Raw(module.CapacityBytes)),
                Field($"memory.{index}.type", t["advanced.type"], module.MemoryType, InfoOrigin.Firmware),
                Field($"memory.{index}.manufacturer", t["cpu.manufacturer"], DisplayNames.MemoryBrand(module.Manufacturer), InfoOrigin.Firmware),
                Field($"memory.{index}.part", t["advanced.partNumber"], module.PartNumber, InfoOrigin.Firmware),
                Field($"memory.{index}.speed", t["advanced.speed"], SpeedText(module), InfoOrigin.Firmware),
                Field($"memory.{index}.voltage", t["advanced.voltage"], Value(module.ConfiguredMillivolts, "0", "mV"), InfoOrigin.Firmware, "mV", Raw(module.ConfiguredMillivolts)),
            };
            groups.Add(Group($"memory.{index}", $"{t["section.memory"]} {module.Slot ?? (index + 1).ToString(CultureInfo.InvariantCulture)}", fieldsForModule));
        }

        groups.Add(Group("memory.spd", t["advanced.spd"], [Missing("memory.spd.value", t["advanced.spd"], InfoState.NotCollected)]));
        return Node("memory", t["section.memory"], groups);
    }

    private static InfoNode Storage(Translator t, HardwareInventory hardware, IReadOnlyList<SensorReading> sensors)
    {
        if (hardware.Disks.Count == 0)
        {
            return Node("storage", t["section.disks"], [Group("storage", t["section.disks"], [Missing("storage", t["section.disks"], InfoState.Unsupported)])]);
        }

        var groups = new List<InfoGroup>();
        for (var index = 0; index < hardware.Disks.Count; index++)
        {
            var disk = hardware.Disks[index];
            var health = hardware.DiskHealth?.FirstOrDefault(item => item.DeviceId == disk.DeviceId);
            var fields = new List<InfoField>
            {
                Field($"storage.{index}.model", t["section.disks"], disk.Model, InfoOrigin.Firmware),
                Field($"storage.{index}.capacity", t["advanced.capacity"], disk.SizeBytes is { } size ? DisplayNames.DiskSize(size) : null, InfoOrigin.Windows, null, Raw(disk.SizeBytes)),
                Field($"storage.{index}.media", t["advanced.media"], disk.MediaType, InfoOrigin.Windows),
                Field($"storage.{index}.bus", t["advanced.bus"], disk.BusType, InfoOrigin.Windows),
                Field($"storage.{index}.system", t["advanced.systemDisk"], disk.IsSystemDisk ? t["common.yes"] : t["common.no"], InfoOrigin.Windows),
                Field($"storage.{index}.pcie", t["advanced.pcieLink"], disk.PcieGeneration is { } generation ? DisplayNames.PcieLink(generation, disk.PcieLanes) : null, InfoOrigin.Windows),
            };

            if (health is not null)
            {
                var healthSource = disk.BusType == "NVMe"
                    ? new InfoProvenance("NVMe", "IOCTL_STORAGE_QUERY_PROPERTY")
                    : new InfoProvenance("DiskInfoToolkit", "SMART");
                fields.Add(Field($"storage.{index}.health", t["disk.status"], t[EnumText.Key("diskHealth", health.Status)], InfoOrigin.Driver, provenance: healthSource));
                fields.Add(Field($"storage.{index}.life", t["disk.life"], health.LifePercent is { } life ? $"{life}%" : null, InfoOrigin.Driver, "%", Raw(health.LifePercent), provenance: healthSource));
                fields.Add(Field($"storage.{index}.temperature", t["disk.temperature"], health.TemperatureCelsius is { } temperature ? $"{temperature}°C" : null, InfoOrigin.Driver, "°C", Raw(health.TemperatureCelsius), provenance: healthSource));
                fields.Add(Field($"storage.{index}.powerOn", t["disk.powerOnHours"], health.PowerOnHours?.ToString("N0", CultureInfo.InvariantCulture), InfoOrigin.Driver, provenance: healthSource));
                fields.Add(Field($"storage.{index}.written", t["disk.dataWritten"], health.DataWrittenBytes is { } written ? DisplayNames.DiskSize(written) : null, InfoOrigin.Driver, null, Raw(health.DataWrittenBytes), provenance: healthSource));
                fields.Add(Field($"storage.{index}.mediaErrors", t["disk.mediaErrors"], health.MediaErrors?.ToString("N0", CultureInfo.InvariantCulture), InfoOrigin.Driver, provenance: healthSource));
            }
            else
            {
                fields.Add(Missing($"storage.{index}.health", t["disk.status"], InfoState.NotCollected));
            }

            fields.Add(Missing($"storage.{index}.volumes", t["advanced.volumes"], InfoState.NotCollected));
            groups.Add(Group($"storage.{index}", t["section.disks"], fields));
        }

        AddSensorGroups(groups, t, hardware, sensors, reading => reading.HardwareType == "Storage", "storage.sensors");
        return Node("storage", t["section.disks"], groups);
    }

    private static InfoNode Displays(Translator t, HardwareInventory hardware)
    {
        if (hardware.Displays.Count == 0)
        {
            return Node("display", t["section.displays"], [Group("display", t["section.displays"], [Missing("display", t["section.displays"], InfoState.Unsupported)])]);
        }

        var groups = new List<InfoGroup>();
        for (var index = 0; index < hardware.Displays.Count; index++)
        {
            var display = hardware.Displays[index];
            var displayConfig = new InfoProvenance("Windows", "DisplayConfig");
            var monitor = new InfoProvenance("Windows", "WMI monitor");
            var fields = new List<InfoField>
            {
                Field($"display.{index}.name", t["section.displays"], display.Name, InfoOrigin.Firmware, provenance: monitor),
                Field($"display.{index}.manufacturer", t["cpu.manufacturer"], display.ManufacturerCode, InfoOrigin.Firmware, provenance: monitor),
                Field($"display.{index}.product", t["advanced.productCode"], display.ProductCode, InfoOrigin.Firmware, provenance: monitor),
                Field($"display.{index}.resolution", t["advanced.resolution"], display.WidthPixels is { } width && display.HeightPixels is { } height ? $"{width} × {height}" : null, InfoOrigin.Windows, provenance: displayConfig),
                Field($"display.{index}.refresh", t["advanced.refreshRate"], display.RefreshRateHz is { } rate ? $"{Math.Round(rate).ToString(CultureInfo.InvariantCulture)} Hz" : null, InfoOrigin.Windows, provenance: displayConfig),
                Field($"display.{index}.diagonal", t["advanced.diagonal"], display.DiagonalInches is { } inches ? $"{inches.ToString("0.#", CultureInfo.InvariantCulture)}" : null, InfoOrigin.Firmware, "in", provenance: monitor),
                Field($"display.{index}.connection", t["advanced.connection"], display.Connection, InfoOrigin.Windows, provenance: displayConfig),
                Field($"display.{index}.primary", t["advanced.primary"], display.IsPrimary ? t["common.yes"] : t["common.no"], InfoOrigin.Windows, provenance: displayConfig),
                Field($"display.{index}.gpu", t["advanced.connectedGpu"], display.ConnectedGpu, InfoOrigin.Windows, provenance: displayConfig),
                Missing($"display.{index}.hdr", t["advanced.hdr"], InfoState.NotCollected),
            };
            groups.Add(Group($"display.{index}", t["section.displays"], fields));
        }

        return Node("display", t["section.displays"], groups);
    }

    private static InfoNode Network(Translator t, HardwareInventory hardware)
    {
        var groups = new List<InfoGroup>();
        var fields = new List<InfoField>();
        if (hardware.Network is { } network)
        {
            var ipHelper = new InfoProvenance("Windows", "IP Helper");
            fields.Add(Field("network.kind", t["summary.network"], t[EnumText.Key("network", network.Kind)], InfoOrigin.Windows, provenance: ipHelper));
            fields.Add(Field("network.adapter", t["advanced.adapter"], network.Adapter, InfoOrigin.Windows, provenance: ipHelper));
            fields.Add(Field("network.link", t["advanced.linkSpeed"], network.LinkMegabits is { } link ? DisplayNames.LinkSpeed(link) : null, InfoOrigin.Windows, null, Raw(network.LinkMegabits), provenance: ipHelper));
            fields.Add(Field("network.signal", t["advanced.signal"], network.SignalPercent is { } signal ? $"{signal}%" : null, InfoOrigin.Windows, "%", Raw(network.SignalPercent), provenance: new InfoProvenance("Windows", "Native Wifi")));
        }
        else
        {
            fields.Add(Missing("network", t["summary.network"], InfoState.NotCollected));
        }

        groups.Add(Group("network", t["summary.network"], fields));
        AddAdapterGroup(groups, t, "network.wired", t["summary.wiredCard"], hardware.WiredAdapters);
        AddAdapterGroup(groups, t, "network.wireless", t["summary.wirelessCard"], hardware.WirelessAdapters);
        groups.Add(Group("network.bluetooth", t["advanced.bluetooth"], [Missing("network.bluetooth.value", t["advanced.bluetooth"], InfoState.NotCollected)]));
        return Node("network", t["summary.network"], groups);
    }

    private static InfoNode Peripherals(Translator t, HardwareInventory hardware)
    {
        var groups = new List<InfoGroup>();
        var fields = new List<InfoField>();
        foreach (var kind in Enum.GetValues<PeripheralKind>())
        {
            var names = (hardware.Peripherals ?? []).Where(item => item.Kind == kind).Select(item =>
            {
                var name = item.Name ?? t["peripheral.unnamed"];
                return item.IsBluetooth ? t["peripheral.viaBluetooth", name] : name;
            }).ToList();
            if (names.Count > 0)
            {
                fields.Add(Field($"peripheral.{kind}", t[EnumText.Key("peripheral", kind)], string.Join(Environment.NewLine, names), InfoOrigin.Windows));
            }
        }

        if (fields.Count == 0)
        {
            fields.Add(Missing("peripheral", t["advanced.other"], InfoState.Unsupported));
        }

        groups.Add(Group("peripheral", t["advanced.peripherals"], fields));
        groups.Add(Group("peripheral.usbPci", t["advanced.usbPci"], [Missing("peripheral.usbPci.value", t["advanced.usbPci"], InfoState.NotCollected)]));
        return Node("peripheral", t["advanced.peripherals"], groups);
    }

    private static InfoNode CoolingAndPower(Translator t, HardwareInventory hardware, IReadOnlyList<SensorReading> sensors)
    {
        var cooling = CoolingEstimator.Estimate(sensors, hardware.Environment.IsSensorAccessLimited);
        var fields = new List<InfoField>
        {
            Field("cooling.outcome", t["cooling.heading"], EnumText.CoolingEstimate(t, cooling.Outcome), InfoOrigin.Inferred),
            Field("cooling.pump", t["ui.fanPump"], Value(cooling.PumpRpm, "0", "RPM"), InfoOrigin.Driver, "RPM", Raw(cooling.PumpRpm)),
            Field("cooling.cpuFan", t["ui.fanCpu"], Value(cooling.CpuFanRpm, "0", "RPM"), InfoOrigin.Driver, "RPM", Raw(cooling.CpuFanRpm)),
            Field("cooling.cooler", t["summary.cooler"], cooling.CoolerName, InfoOrigin.Driver),
        };

        if (hardware.Battery is { } battery)
        {
            fields.Add(Field("battery.health", t["summary.battery"], t["summary.batteryValue", battery.HealthPercent, DisplayNames.WattHours(battery.FullMilliwattHours), DisplayNames.WattHours(battery.DesignedMilliwattHours)], InfoOrigin.Firmware));
        }

        fields.Add(Missing("power.manual", t["advanced.psuCaseCooler"], InfoState.NotCollected));
        return Node("cooling", t["advanced.coolingPower"], [Group("cooling", t["advanced.coolingPower"], fields)]);
    }

    private static InfoNode Windows(Translator t, HardwareInventory hardware)
    {
        var environment = hardware.Environment;
        var fields = new List<InfoField>();
        if (environment.OperatingSystem is { } os)
        {
            fields.Add(Field("windows.name", t["summary.system"], Join(os.Name, os.DisplayVersion), InfoOrigin.Windows));
            fields.Add(Field("windows.build", t["advanced.build"], os.Build, InfoOrigin.Windows));
            fields.Add(Field("windows.architecture", t["advanced.architecture"], os.Architecture, InfoOrigin.Windows));
            fields.Add(Field("windows.installDate", t["summary.installDate"], os.InstallDate, InfoOrigin.Windows));
        }

        fields.Add(Field("windows.activation", t["advanced.activation"], t[EnumText.Key("activation", environment.Activation)], InfoOrigin.Windows, provenance: new InfoProvenance("Windows", "Software Licensing Service")));
        fields.Add(Field("windows.secureBoot", t["summary.secureBoot"], hardware.Security?.SecureBootEnabled switch
        {
            true => t["summary.enabled"],
            false => t["summary.disabled"],
            _ => null,
        }, InfoOrigin.Firmware, provenance: new InfoProvenance("Windows", "Registry")));
        fields.Add(Field("windows.tpm", t["advanced.tpm"], hardware.Security?.TpmVersion, InfoOrigin.Firmware, provenance: new InfoProvenance("Windows", "TBS")));
        if (hardware.Locale is { } locale)
        {
            fields.Add(Field("windows.language", t["summary.language"], locale.DisplayLanguage, InfoOrigin.Windows));
            fields.Add(Field("windows.encoding", t["summary.encoding"], DisplayNames.EncodingName(locale.AnsiCodePage), InfoOrigin.Windows));
        }

        fields.Add(Field("windows.whea", t["summary.whea"], HardwareSummary.WheaText(t, hardware.Reliability), InfoOrigin.Windows));
        fields.Add(Missing("windows.updates", t["advanced.windowsUpdates"], InfoState.NotCollected));
        return Node("windows", t["summary.system"], [Group("windows", t["summary.system"], fields)]);
    }

    private static InfoNode SoftwareAndDrivers(Translator t, HardwareInventory hardware)
    {
        var fields = new List<InfoField>();
        foreach (var gpu in hardware.Gpus.Where(item => item.Kind is not (GpuKind.Software or GpuKind.Integrated)))
        {
            fields.Add(Field($"software.driver.{gpu.Model}", t["summary.gpuDriver"], DriverText(gpu), InfoOrigin.Driver));
        }

        fields.Add(Missing("software.drivers", t["advanced.driverList"], InfoState.NotCollected));
        fields.Add(Missing("software.installed", t["advanced.installedSoftware"], InfoState.NotCollected));
        return Node("software", t["advanced.softwareDrivers"], [Group("software", t["advanced.softwareDrivers"], fields)]);
    }

    private static InfoNode EventLog(Translator t, HardwareInventory hardware)
    {
        var fields = new List<InfoField>
        {
            Field("log.whea", t["summary.whea"], HardwareSummary.WheaText(t, hardware.Reliability), InfoOrigin.Windows),
            Missing("log.details", t["advanced.eventLogDetails"], InfoState.NotCollected),
        };
        return Node("log", t["advanced.eventLog"], [Group("log", t["advanced.eventLog"], fields)]);
    }

    private static InfoNode Other(Translator t, HardwareInventory hardware)
    {
        var fields = hardware.Diagnostics.Select((diagnostic, index) =>
            Field($"diagnostic.{index}", diagnostic.Source, diagnostic.Message, InfoOrigin.Windows)).ToList();
        if (fields.Count == 0)
        {
            fields.Add(Field("diagnostic.none", t["section.diagnostics"], t["advanced.noDiagnostics"], InfoOrigin.Windows));
        }

        return Node("other", t["advanced.other"], [Group("other", t["advanced.other"], fields)]);
    }

    private static void AddAdapterGroup(List<InfoGroup> groups, Translator t, string key, string label, IReadOnlyList<string>? adapters)
    {
        var values = adapters ?? [];
        var fields = values.Count == 0
            ? new List<InfoField> { Missing($"{key}.value", label, InfoState.Unsupported) }
            : values.Select((name, index) => Field($"{key}.{index}", label, name, InfoOrigin.Windows)).ToList();
        groups.Add(Group(key, label, fields));
    }

    private static void AddSensorGroups(List<InfoGroup> groups, Translator t, HardwareInventory hardware, IReadOnlyList<SensorReading> sensors, Func<SensorReading, bool> predicate, string keyPrefix)
    {
        var matched = sensors.Where(predicate).GroupBy(reading => reading.HardwareName).ToList();
        if (matched.Count == 0)
        {
            var state = hardware.Environment.IsSensorAccessLimited ? InfoState.PermissionDenied : InfoState.NotCollected;
            groups.Add(Group(keyPrefix, t["advanced.sensors"], [Missing($"{keyPrefix}.missing", t["advanced.sensors"], state)]));
            return;
        }

        for (var index = 0; index < matched.Count; index++)
        {
            var fields = matched[index].Select(ToSensorField).ToList();
            groups.Add(Group($"{keyPrefix}.{index}", $"{t["advanced.sensors"]}: {matched[index].Key}", fields));
        }
    }

    private static InfoField ToSensorField(SensorReading reading)
    {
        var state = reading.Status switch
        {
            SensorStatus.Ok => InfoState.Available,
            SensorStatus.NoValue => InfoState.NotCollected,
            _ => InfoState.ReadFailed,
        };
        var value = state == InfoState.Available && reading.Value is { } number
            ? $"{number.ToString("0.###", CultureInfo.InvariantCulture)} {reading.Unit}".Trim()
            : null;
        return new InfoField(
            $"sensor.{reading.SensorId}",
            $"{reading.SensorName} ({reading.SensorType})",
            value,
            reading.Value?.ToString("R", CultureInfo.InvariantCulture),
            reading.Unit,
            state,
            InfoOrigin.Driver,
            new InfoProvenance("LibreHardwareMonitorLib", "LHM", RequiresDriver: true, IsRefreshable: true));
    }

    private static InfoField Field(
        string key,
        string label,
        string? value,
        InfoOrigin origin = InfoOrigin.Windows,
        string? unit = null,
        string? raw = null,
        string? note = null,
        InfoProvenance? provenance = null) =>
        value is null
            ? Missing(key, label, InfoState.NotCollected, note)
            : new InfoField(key, label, value, raw, unit, InfoState.Available, origin, provenance ?? DefaultProvenance(origin), note);

    private static InfoField Missing(string key, string label, InfoState state, string? note = null) =>
        new(key, label, State: state, Note: note);

    private static InfoProvenance DefaultProvenance(InfoOrigin origin) => origin switch
    {
        InfoOrigin.Firmware => new("Windows", "Win32 / WMI"),
        InfoOrigin.Driver => new("Driver", "Device driver API"),
        InfoOrigin.Windows => new("Windows", "Win32 / WMI"),
        InfoOrigin.Calculated => new("This tool", "Calculated"),
        InfoOrigin.Inferred => new("This tool", "Inferred"),
        _ => new("Manual", "User input"),
    };

    private static InfoNode Node(string key, string label, IReadOnlyList<InfoGroup> groups) => new(key, label, groups);

    private static InfoGroup Group(string key, string label, IReadOnlyList<InfoField> fields) => new(key, label, fields);

    private static string? Join(params string?[] parts)
    {
        var known = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        return known.Count == 0 ? null : string.Join(" ", known);
    }

    private static string? Value(double? value, string format, string unit) =>
        value is { } number ? $"{number.ToString(format, CultureInfo.InvariantCulture)} {unit}" : null;

    private static string? Raw(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    private static string? Raw(ulong? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Raw(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? CacheText(int? kilobytes) =>
        kilobytes is { } size and > 0 ? $"{(size / KILOBYTES_PER_MEGABYTE).ToString("0.#", CultureInfo.InvariantCulture)} MB" : null;

    private static string? SpeedText(MemoryModule module)
    {
        string? rated = module.RatedSpeedMts is { } ratedValue ? $"{ratedValue} MT/s" : null;
        string? configured = module.ConfiguredSpeedMts is { } configuredValue ? $"{configuredValue} MT/s" : null;
        return rated == configured ? configured : Join(rated, configured);
    }

    private static string? DriverText(GpuInfo gpu)
    {
        var parts = new[] { gpu.DriverProvider, gpu.DriverVersion, gpu.DriverDate };
        var known = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        return known.Count == 0 ? null : string.Join(" ", known);
    }

    private static string AccessText(Translator t, ProbeEnvironment environment) =>
        t["environment.access", environment.IsAdministrator ? t["common.yes"] : t["common.no"], environment.IsPawnIoInstalled ? t["common.yes"] : t["common.no"]];
}
