using System.Net.NetworkInformation;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// Finds how this PC reaches the network now: Wi-Fi, wired or not at all. Nothing is sent over the network,
/// and addresses, MAC numbers and network names are never read into the report.
/// </summary>
public static class NetworkCollector
{
    private const long BITS_PER_MEGABIT = 1_000_000;

    private static readonly string[] VirtualNameParts =
    [
        "Hyper-V", "Virtual", "VMware", "VirtualBox", "TAP-", "WAN Miniport", "Bluetooth", "Loopback", "Filter", "QoS", "VPN", "Tunnel", "WireGuard", "Tailscale",
    ];

    private static readonly string[] WirelessNameParts = ["Wi-Fi", "WiFi", "Wireless", "WLAN", "802.11", "无线", "無線", "ワイヤレス", "무선"];

    public static NetworkConnection Read()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces().Select(ToCandidate).ToList();
        var connection = Choose(candidates);
        return connection.Kind == NetworkKind.WiFi ? connection with { SignalPercent = WifiSignalReader.Read() } : connection;
    }

    /// <summary>Only a physical adapter that is up with a gateway counts. Wired wins when both are connected, as Windows prefers it.</summary>
    public static NetworkConnection Choose(IEnumerable<NetworkCandidate> candidates)
    {
        var usable = candidates.Where(IsConnectedPhysical).ToList();
        var chosen = usable.FirstOrDefault(item => item.Kind == NetworkInterfaceType.Ethernet)
            ?? usable.FirstOrDefault(item => item.Kind == NetworkInterfaceType.Wireless80211);

        return chosen is null
            ? new NetworkConnection(NetworkKind.None, null, null)
            : new NetworkConnection(
                chosen.Kind == NetworkInterfaceType.Ethernet ? NetworkKind.Wired : NetworkKind.WiFi,
                chosen.Description,
                chosen.SpeedBitsPerSecond > 0 ? chosen.SpeedBitsPerSecond / BITS_PER_MEGABIT : null);
    }

    /// <summary>Wireless card names state it, unlike wired ones: "Wi-Fi 6 AX201", "Wireless LAN", "WLAN", "802.11".</summary>
    public static bool IsWirelessCard(string name) =>
        WirelessNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    public static bool IsPhysical(string description) =>
        !VirtualNameParts.Any(part => description.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static bool IsConnectedPhysical(NetworkCandidate candidate) =>
        candidate.IsUp
        && candidate.HasGateway
        && candidate.Kind is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
        && IsPhysical(candidate.Description);

    private static NetworkCandidate ToCandidate(NetworkInterface adapter)
    {
        var isUp = adapter.OperationalStatus == OperationalStatus.Up;
        return new NetworkCandidate(
            adapter.Description,
            adapter.NetworkInterfaceType,
            isUp,
            isUp && adapter.GetIPProperties().GatewayAddresses.Count > 0,
            adapter.Speed);
    }
}

public sealed record NetworkCandidate(string Description, NetworkInterfaceType Kind, bool IsUp, bool HasGateway, long SpeedBitsPerSecond);
