using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Monitoring;

public enum MetricLevel
{
    Normal,
    Warning,
    Alert,
}

/// <summary>
/// Decides when a live reading changes color. Windows publishes no ranges for this, so these are this tool's own:
/// usage follows the common 80% and 95% marks, and temperature ends at the same limit that stops a stress test.
/// </summary>
public static class MetricLevels
{
    private const double USAGE_WARNING_FRACTION = 0.8;
    private const double USAGE_ALERT_FRACTION = 0.95;
    private const double TEMPERATURE_WARNING_CELSIUS = 85;
    private const double BITS_PER_BYTE = 8;
    private const double BITS_PER_MEGABIT = 1_000_000;

    public static MetricLevel ForUsage(double? fraction) =>
        fraction >= USAGE_ALERT_FRACTION ? MetricLevel.Alert
        : fraction >= USAGE_WARNING_FRACTION ? MetricLevel.Warning
        : MetricLevel.Normal;

    public static MetricLevel ForTemperature(double? celsius, double protectionCelsius) =>
        celsius >= protectionCelsius ? MetricLevel.Alert
        : celsius >= Math.Min(TEMPERATURE_WARNING_CELSIUS, protectionCelsius) ? MetricLevel.Warning
        : MetricLevel.Normal;

    /// <summary>Network speed as a share of the connection's link speed. Null when no connection speed is known.</summary>
    public static double? LinkFraction(double? bytesPerSecond, NetworkConnection? network) =>
        bytesPerSecond is { } rate && network?.LinkMegabits is > 0
            ? rate * BITS_PER_BYTE / (network.LinkMegabits.Value * BITS_PER_MEGABIT)
            : null;

    /// <summary>
    /// Network activity for the bar. A linear share of a gigabit link leaves ordinary traffic at zero pixels, so the
    /// bar uses a logarithmic scale of that same link-speed share; the text still shows the real bytes per second.
    /// Null means no link speed was read, and the caller keeps an empty track.
    /// </summary>
    public static double? LinkActivity(double? bytesPerSecond, NetworkConnection? network)
    {
        if (bytesPerSecond is not { } rate || network?.LinkMegabits is not > 0)
        {
            return null;
        }

        if (rate <= 0)
        {
            return 0;
        }

        var linkBytesPerSecond = network.LinkMegabits.Value * BITS_PER_MEGABIT / BITS_PER_BYTE;
        return Math.Clamp(Math.Log10(1 + rate) / Math.Log10(1 + linkBytesPerSecond), 0, 1);
    }
}
