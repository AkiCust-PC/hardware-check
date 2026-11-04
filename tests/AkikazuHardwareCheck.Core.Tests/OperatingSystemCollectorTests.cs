using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Tests;

public class OperatingSystemCollectorTests
{
    [Theory]
    [InlineData(1, WindowsActivation.Activated)]
    [InlineData(0, WindowsActivation.NotActivated)]
    [InlineData(2, WindowsActivation.GracePeriod)]
    [InlineData(3, WindowsActivation.GracePeriod)]
    [InlineData(4, WindowsActivation.GracePeriod)]
    [InlineData(6, WindowsActivation.GracePeriod)]
    [InlineData(5, WindowsActivation.Notification)]
    [InlineData(99, WindowsActivation.Unknown)]
    public void LicenseStatusMapsToActivation(int status, WindowsActivation expected)
    {
        Assert.Equal(expected, OperatingSystemCollector.MapActivation([status]));
    }

    [Fact]
    public void AnyLicensedProductMeansActivated()
    {
        Assert.Equal(WindowsActivation.Activated, OperatingSystemCollector.MapActivation([0, 5, 1]));
    }

    [Fact]
    public void NoInstalledKeyIsUnknownRatherThanGuessed()
    {
        Assert.Equal(WindowsActivation.Unknown, OperatingSystemCollector.MapActivation([]));
        Assert.Equal(WindowsActivation.Unknown, OperatingSystemCollector.MapActivation([null]));
    }

    [Theory]
    [InlineData("26200", 7171, "26200.7171")]
    [InlineData("26200", null, "26200")]
    [InlineData(null, 7171, null)]
    public void BuildIncludesUpdateRevisionWhenKnown(string? build, int? updateRevision, string? expected)
    {
        Assert.Equal(expected, OperatingSystemCollector.FormatBuild(build, updateRevision));
    }
}
