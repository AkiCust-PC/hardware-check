using System.Management;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// Identifies laptops and branded PCs, and reads battery wear. Serial numbers and UUIDs are never selected.
/// Self-built PCs report placeholder text here and simply get no model line.
/// </summary>
public static class ComputerCollector
{
    // SMBIOS chassis types for portable computers: laptop, notebook, hand held, sub notebook, tablet, convertible, detachable and similar.
    private static readonly HashSet<int> PortableChassisTypes = [8, 9, 10, 11, 14, 30, 31, 32];

    private static readonly string[] PlaceholderParts =
    [
        "System Product Name", "System manufacturer", "System Version", "Default string", "To Be Filled", "O.E.M", "Not Specified", "Not Applicable", "None",
    ];

    private const int FULL_PERCENT = 100;

    public static ComputerInfo? Read()
    {
        var system = Wmi.Query(
            Wmi.CIM_SCOPE,
            "SELECT Manufacturer, Model FROM Win32_ComputerSystem",
            item => (Manufacturer: Wmi.Text(item, "Manufacturer"), Model: Wmi.Text(item, "Model")))
            .FirstOrDefault();
        var version = Wmi.Query(Wmi.CIM_SCOPE, "SELECT Version FROM Win32_ComputerSystemProduct", item => Wmi.Text(item, "Version")).FirstOrDefault();
        var chassis = Wmi.Query(Wmi.CIM_SCOPE, "SELECT ChassisTypes FROM Win32_SystemEnclosure", item => item["ChassisTypes"] as ushort[]).FirstOrDefault() ?? [];

        return Describe(system.Manufacturer, system.Model, version, chassis.Select(type => (int)type));
    }

    /// <summary>Lenovo stores a machine code such as 20XW in Model and the product name in Version.</summary>
    public static ComputerInfo? Describe(string? manufacturer, string? model, string? version, IEnumerable<int> chassisTypes)
    {
        var isLaptop = chassisTypes.Any(PortableChassisTypes.Contains);
        var isLenovo = manufacturer?.StartsWith("LENOVO", StringComparison.OrdinalIgnoreCase) == true;
        var name = isLenovo && !IsPlaceholder(version) ? version : model;
        if (IsPlaceholder(name) || IsPlaceholder(manufacturer))
        {
            return isLaptop ? new ComputerInfo(null, null, true) : null;
        }

        return new ComputerInfo(manufacturer, name, isLaptop);
    }

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || PlaceholderParts.Any(part => value.Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <summary>Desktops have no battery classes at all, which is an answer rather than a failure.</summary>
    public static BatteryInfo? ReadBattery()
    {
        try
        {
            var designed = Wmi.Query(Wmi.MONITOR_SCOPE, "SELECT DesignedCapacity FROM BatteryStaticData", item => Wmi.Number(item, "DesignedCapacity")).FirstOrDefault();
            var full = Wmi.Query(Wmi.MONITOR_SCOPE, "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", item => Wmi.Number(item, "FullChargedCapacity")).FirstOrDefault();
            return designed is > 0 && full is not null ? new BatteryInfo(designed.Value, full.Value, HealthPercent(designed.Value, full.Value)) : null;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    public static int HealthPercent(ulong designedMilliwattHours, ulong fullMilliwattHours) =>
        designedMilliwattHours == 0 ? 0 : (int)Math.Min(FULL_PERCENT, fullMilliwattHours * FULL_PERCENT / designedMilliwattHours);
}
