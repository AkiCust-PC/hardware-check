using System.Text.RegularExpressions;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// Lists connected devices by the names Windows reports. Device instance IDs can hold serial numbers,
/// so they are used only to classify and never kept. Whether a USB receiver is wireless cannot be told reliably.
/// </summary>
public static partial class PeripheralCollector
{
    private const string QUERY = """
        SELECT Name, PNPClass, PNPDeviceID, Status, HardwareID FROM Win32_PnPEntity
        WHERE PNPClass = 'Mouse' OR PNPClass = 'Keyboard' OR PNPClass = 'Camera' OR PNPClass = 'Image' OR PNPClass = 'Printer' OR PNPClass = 'HIDClass'
        """;

    private const string NETWORK_QUERY = "SELECT Name, PNPDeviceID, Status FROM Win32_PnPEntity WHERE PNPClass = 'Net'";

    // HID usage page and usage in Windows hardware IDs, which do not change with the system language.
    private const string HID_TOUCH_SCREEN = "UP:000D_U:0004";
    private const string HID_TOUCH_PAD = "UP:000D_U:0005";
    private const string HID_JOYSTICK = "UP:0001_U:0004";
    private const string HID_GAMEPAD = "UP:0001_U:0005";

    // Laptop drivers can list the touchpad under the Mouse class with their own name.
    private static readonly string[] TouchpadNameParts = ["Touchpad", "Touch Pad", "Pointing Device", "TrackPoint", "Precision", "触控板", "触摸板", "タッチパッド"];

    private static readonly string[] GenericNameParts = ["HID", "USB 输入", "USB 輸入", "USB Input", "USB 入力", "USB 입력"];
    private static readonly string[] NetworkBuses = [@"PCI\", @"USB\"];

    public static List<Peripheral> Read()
    {
        var devices = Wmi.Query(
            Wmi.CIM_SCOPE,
            QUERY,
            item => (Status: Wmi.Text(item, "Status"), Name: Wmi.Text(item, "Name"), Class: Wmi.Text(item, "PNPClass"), Id: Wmi.Text(item, "PNPDeviceID"), HardwareIds: item["HardwareID"] as string[]))
            .Where(item => item.Status == "OK")
            .Select(item => Classify(item.Name, item.Class, item.Id, item.HardwareIds))
            .OfType<Peripheral>();

        var audio = new[]
        {
            (Kind: PeripheralKind.Speaker, Device: AudioDeviceReader.ReadSpeaker()),
            (Kind: PeripheralKind.Microphone, Device: AudioDeviceReader.ReadMicrophone()),
        }
        .Where(item => item.Device is not null)
        .Select(item => new Peripheral(item.Kind, item.Device!.Name, item.Device.IsBluetooth));

        return Normalize(devices.Concat(audio));
    }

    public static List<string> ReadNetworkAdapters() => ReadNames(NETWORK_QUERY, IsNetworkCard);

    /// <summary>Real network cards sit on PCI or USB. Wi-Fi Direct, VPN and Hyper-V adapters are software devices.</summary>
    public static bool IsNetworkCard(string? name, string? deviceId) =>
        !string.IsNullOrEmpty(name)
        && OnBus(deviceId, NetworkBuses)
        && !name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns null for devices that are not useful to staff.</summary>
    public static Peripheral? Classify(string? name, string? deviceClass, string? deviceId, IReadOnlyList<string>? hardwareIds = null)
    {
        var id = deviceId ?? "";
        var cleaned = CleanName(name);
        var isBluetooth = id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) || id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) || id.Contains("BTHLE", StringComparison.OrdinalIgnoreCase);
        return deviceClass switch
        {
            // A gaming mouse also registers a keyboard interface and vice versa, so the two cannot be told apart reliably.
            "Mouse" or "Keyboard" when IsTouchpadName(cleaned) => new Peripheral(PeripheralKind.Touchpad, cleaned, isBluetooth),
            "Mouse" or "Keyboard" => new Peripheral(PeripheralKind.Input, IsGenericInputName(cleaned) ? null : cleaned, isBluetooth),
            "HIDClass" => ClassifyHid(cleaned, hardwareIds, isBluetooth),
            "Camera" => new Peripheral(PeripheralKind.Camera, cleaned, isBluetooth),
            "Image" or "Printer" => new Peripheral(PeripheralKind.Printer, cleaned, isBluetooth),
            _ => null,
        };
    }

    private static Peripheral? ClassifyHid(string? name, IReadOnlyList<string>? hardwareIds, bool isBluetooth)
    {
        bool Has(string usage) => hardwareIds?.Any(id => id.Contains(usage, StringComparison.OrdinalIgnoreCase)) == true;
        PeripheralKind? kind = Has(HID_TOUCH_PAD) ? PeripheralKind.Touchpad
            : Has(HID_TOUCH_SCREEN) ? PeripheralKind.Touchscreen
            : Has(HID_GAMEPAD) || Has(HID_JOYSTICK) ? PeripheralKind.Gamepad
            : null;
        return kind is { } value ? new Peripheral(value, IsGenericInputName(name) ? null : name, isBluetooth) : null;
    }

    private static bool IsTouchpadName(string? name) =>
        name is not null && TouchpadNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <summary>Merges duplicates and hides generic entries when a named device of that kind exists.</summary>
    public static List<Peripheral> Normalize(IEnumerable<Peripheral> items)
    {
        var distinct = items.Distinct().ToList();
        var namedKinds = distinct.Where(item => item.Name is not null).Select(item => item.Kind).ToHashSet();
        return distinct
            .Where(item => item.Name is not null || !namedKinds.Contains(item.Kind))
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Removes printers' hardware address fragments and the wordy driver note on monitor audio.</summary>
    public static string? CleanName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var cleaned = AddressFragmentPattern().Replace(name, "");
        cleaned = DisplayAudioPattern().Replace(cleaned, "");
        return DisplayNames.CleanModel(cleaned);
    }

    private static List<string> ReadNames(string query, Func<string?, string?, bool> isWanted) => Wmi.Query(
        Wmi.CIM_SCOPE,
        query,
        item => (Status: Wmi.Text(item, "Status"), Name: Wmi.Text(item, "Name"), Id: Wmi.Text(item, "PNPDeviceID")))
        .Where(item => item.Status == "OK" && isWanted(item.Name, item.Id))
        .Select(item => DisplayNames.CleanModel(item.Name!))
        .Distinct()
        .ToList();

    private static bool OnBus(string? deviceId, string[] buses) =>
        deviceId is not null && buses.Any(bus => deviceId.StartsWith(bus, StringComparison.OrdinalIgnoreCase));

    private static bool IsGenericInputName(string? name) =>
        name is null || GenericNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\s*\([0-9a-f]{2}(:[0-9a-f]{2})+\)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressFragmentPattern();

    [GeneratedRegex(@"\s*\([^()]*Display Audio\)", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayAudioPattern();
}
