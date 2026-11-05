using System.Globalization;
using System.Management;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Reads CPU, motherboard, memory and disk details. Queries never select serial numbers.</summary>
public static class SystemInfoCollector
{
    // Values from the SMBIOS memory device type table and MSFT_PhysicalDisk documentation.
    private const int SMBIOS_DDR3 = 24;
    private const int SMBIOS_DDR4 = 26;
    private const int SMBIOS_LPDDR4 = 30;
    private const int SMBIOS_DDR5 = 34;
    private const int SMBIOS_LPDDR5 = 35;

    public static List<CpuInfo> ReadCpus() => Wmi.Query(
        Wmi.CIM_SCOPE,
        "SELECT Manufacturer, Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, SocketDesignation, L2CacheSize, L3CacheSize, Caption, VirtualizationFirmwareEnabled FROM Win32_Processor",
        item => new CpuInfo(
            Wmi.Text(item, "Manufacturer"),
            Wmi.Text(item, "Name"),
            Wmi.Integer(item, "NumberOfCores"),
            Wmi.Integer(item, "NumberOfLogicalProcessors"),
            Wmi.Integer(item, "MaxClockSpeed"),
            Wmi.Text(item, "SocketDesignation"),
            Wmi.Integer(item, "L2CacheSize"),
            Wmi.Integer(item, "L3CacheSize"),
            Wmi.Text(item, "Caption"),
            item["VirtualizationFirmwareEnabled"] as bool?));

    public static MotherboardInfo ReadMotherboard()
    {
        var board = Wmi.Query(
            Wmi.CIM_SCOPE,
            "SELECT Manufacturer, Product FROM Win32_BaseBoard",
            item => (Manufacturer: Wmi.Text(item, "Manufacturer"), Model: Wmi.Text(item, "Product")))
            .FirstOrDefault();
        var bios = Wmi.Query(
            Wmi.CIM_SCOPE,
            "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
            item => (Version: Wmi.Text(item, "SMBIOSBIOSVersion"), Date: FormatWmiDate(Wmi.Text(item, "ReleaseDate"))))
            .FirstOrDefault();

        return new MotherboardInfo(board.Manufacturer, board.Model, bios.Version, bios.Date);
    }

    public static MemoryInfo ReadMemory()
    {
        var systemVisible = Wmi.Query(
            Wmi.CIM_SCOPE,
            "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem",
            item => Wmi.Number(item, "TotalPhysicalMemory"))
            .FirstOrDefault();
        var modules = Wmi.Query(
            Wmi.CIM_SCOPE,
            "SELECT DeviceLocator, Capacity, SMBIOSMemoryType, Manufacturer, PartNumber, Speed, ConfiguredClockSpeed, ConfiguredVoltage FROM Win32_PhysicalMemory",
            item => new MemoryModule(
                Wmi.Text(item, "DeviceLocator"),
                Wmi.Number(item, "Capacity"),
                MapMemoryType(Wmi.Integer(item, "SMBIOSMemoryType")),
                Wmi.Text(item, "Manufacturer"),
                Wmi.Text(item, "PartNumber"),
                PositiveOrNull(Wmi.Integer(item, "Speed")),
                PositiveOrNull(Wmi.Integer(item, "ConfiguredClockSpeed")),
                PositiveOrNull(Wmi.Integer(item, "ConfiguredVoltage"))));

        var installed = modules.Count > 0 && modules.All(module => module.CapacityBytes is not null)
            ? modules.Aggregate(0UL, (total, module) => total + module.CapacityBytes!.Value)
            : (ulong?)null;

        return new MemoryInfo(systemVisible, installed, modules);
    }

    public static List<DiskInfo> ReadDisks()
    {
        var systemDisk = ReadSystemDiskNumber();
        var deviceIds = Wmi.Query(
            Wmi.CIM_SCOPE,
            "SELECT Index, PNPDeviceID FROM Win32_DiskDrive",
            item => (Index: Wmi.Number(item, "Index")?.ToString(), Device: Wmi.Text(item, "PNPDeviceID")));
        return Wmi.Query(
            Wmi.STORAGE_SCOPE,
            "SELECT DeviceId, FriendlyName, Size, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk",
            item =>
            {
                var deviceId = Wmi.Text(item, "DeviceId");
                var instance = deviceIds.FirstOrDefault(entry => entry.Index == deviceId).Device;
                var link = instance is null ? default : PcieLinkReader.Read(instance);
                return new DiskInfo(
                    deviceId,
                    Wmi.Text(item, "FriendlyName"),
                    Wmi.Number(item, "Size"),
                    MapMediaType(Wmi.Integer(item, "MediaType")),
                    MapBusType(Wmi.Integer(item, "BusType")),
                    MapHealth(Wmi.Integer(item, "HealthStatus")),
                    deviceId is not null && deviceId == systemDisk,
                    link.Generation,
                    link.Lanes);
            });
    }

    public static string? MapMemoryType(int? smbiosType) => smbiosType switch
    {
        SMBIOS_DDR3 => "DDR3",
        SMBIOS_DDR4 => "DDR4",
        SMBIOS_LPDDR4 => "LPDDR4",
        SMBIOS_DDR5 => "DDR5",
        SMBIOS_LPDDR5 => "LPDDR5",
        _ => null,
    };

    private static string? ReadSystemDiskNumber()
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive")?.FirstOrDefault();
        if (systemDrive is null or '\0')
        {
            return null;
        }

        return Wmi.Query(
            Wmi.STORAGE_SCOPE,
            "SELECT DiskNumber, DriveLetter FROM MSFT_Partition",
            item => (Letter: item["DriveLetter"] is { } letter ? Convert.ToChar(letter) : '\0', Disk: Wmi.Number(item, "DiskNumber")))
            .Where(partition => char.ToUpperInvariant(partition.Letter) == char.ToUpperInvariant(systemDrive.Value))
            .Select(partition => partition.Disk?.ToString())
            .FirstOrDefault();
    }

    internal static string? FormatWmiDate(string? wmiDate)
    {
        if (wmiDate is null)
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(wmiDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int? PositiveOrNull(int? value) => value > 0 ? value : null;

    private static string? MapMediaType(int? mediaType) => mediaType switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => null,
    };

    private static string? MapBusType(int? busType) => busType switch
    {
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        17 => "NVMe",
        _ => null,
    };

    private static string? MapHealth(int? health) => health switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => null,
    };
}
