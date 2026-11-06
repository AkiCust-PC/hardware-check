using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.Core.Tests;

public class MetricLevelsTests
{
    [Theory]
    [InlineData(null, MetricLevel.Normal)]
    [InlineData(0.79, MetricLevel.Normal)]
    [InlineData(0.8, MetricLevel.Warning)]
    [InlineData(0.94, MetricLevel.Warning)]
    [InlineData(0.95, MetricLevel.Alert)]
    public void UsageChangesAtTheCommonMarks(double? fraction, MetricLevel expected)
    {
        Assert.Equal(expected, MetricLevels.ForUsage(fraction));
    }

    [Theory]
    [InlineData(null, MetricLevel.Normal)]
    [InlineData(84d, MetricLevel.Normal)]
    [InlineData(85d, MetricLevel.Warning)]
    [InlineData(99d, MetricLevel.Warning)]
    [InlineData(100d, MetricLevel.Alert)]
    public void TemperatureTurnsRedAtTheProtectionLimit(double? celsius, MetricLevel expected)
    {
        Assert.Equal(expected, MetricLevels.ForTemperature(celsius, 100));
    }

    [Fact]
    public void ALimitBelowTheWarningMarkStillTurnsRedAtTheLimit()
    {
        Assert.Equal(MetricLevel.Normal, MetricLevels.ForTemperature(69, 70));
        Assert.Equal(MetricLevel.Alert, MetricLevels.ForTemperature(70, 70));
    }

    [Fact]
    public void NetworkSpeedIsMeasuredAgainstTheLink()
    {
        var gigabit = new NetworkConnection(NetworkKind.Wired, "x", 1000);

        Assert.Equal(0.5, MetricLevels.LinkFraction(62_500_000, gigabit));
        Assert.Null(MetricLevels.LinkFraction(null, gigabit));
        Assert.Null(MetricLevels.LinkFraction(1000, new NetworkConnection(NetworkKind.None, null, null)));
    }

    [Fact]
    public void SmallTrafficStillMovesTheNetworkBar()
    {
        var wifi = new NetworkConnection(NetworkKind.WiFi, "x", 2882);

        Assert.True(MetricLevels.LinkActivity(2_000, wifi) > 0.2);
        Assert.Equal(0, MetricLevels.LinkActivity(0, wifi));
        Assert.Null(MetricLevels.LinkActivity(null, wifi));
        Assert.Null(MetricLevels.LinkActivity(1000, new NetworkConnection(NetworkKind.None, null, null)));
    }
}
