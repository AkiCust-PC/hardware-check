using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Tests;

public class CoolingEstimatorTests
{
    private static SensorReading Fan(
        string name,
        double? rpm,
        string hardwareType = "SuperIO",
        SensorStatus status = SensorStatus.Ok) =>
        new("/hw", "hardware", hardwareType, $"/hw/fan/{name}", name, "Fan", rpm, "RPM", status, "test", DateTimeOffset.UnixEpoch);

    private static CoolingEstimate Estimate(params SensorReading[] sensors) => CoolingEstimator.Estimate(sensors, false);

    [Fact]
    public void PumpAndCpuFanMeanLikelyLiquid()
    {
        var result = Estimate(Fan("AIO Pump", 2800), Fan("CPU Fan", 900));

        Assert.Equal(CoolingOutcome.LikelyLiquid, result.Outcome);
        Assert.Equal(2800, result.PumpRpm);
        Assert.Equal(900, result.CpuFanRpm);
        Assert.Contains(CoolingNote.PumpHeaderIsNotProof, result.Notes);
    }

    [Fact]
    public void CpuFanWithoutPumpMeansLikelyAir()
    {
        var result = Estimate(Fan("CPU Fan", 1100));

        Assert.Equal(CoolingOutcome.LikelyAir, result.Outcome);
        Assert.Null(result.PumpRpm);
        Assert.Contains(CoolingNote.PumpMayBeUnreported, result.Notes);
    }

    [Fact]
    public void PumpWithoutCpuFanAsksStaffToCheckWiring()
    {
        var result = Estimate(Fan("Pump", 2500, "Cooler"));

        Assert.Equal(CoolingOutcome.AbnormalWiring, result.Outcome);
        Assert.Contains(CoolingNote.CheckCoolerWiring, result.Notes);
    }

    [Fact]
    public void NoSensorsMeansNoDataAndMentionsFanlessSystems()
    {
        var result = Estimate();

        Assert.Equal(CoolingOutcome.NoData, result.Outcome);
        Assert.Contains(CoolingNote.PassiveCoolingPossible, result.Notes);
        Assert.DoesNotContain(CoolingNote.CheckCoolerWiring, result.Notes);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(null)]
    public void StoppedOrEmptyPumpDoesNotCount(double? pumpRpm)
    {
        var result = Estimate(Fan("Pump", pumpRpm), Fan("CPU Fan", 1000));

        Assert.Equal(CoolingOutcome.LikelyAir, result.Outcome);
    }

    [Fact]
    public void InvalidReadingsDoNotCount()
    {
        var result = Estimate(Fan("Pump", 65535, status: SensorStatus.Invalid), Fan("CPU Fan", 65535, status: SensorStatus.Invalid));

        Assert.Equal(CoolingOutcome.NoData, result.Outcome);
    }

    [Fact]
    public void GpuFansAreIgnored()
    {
        var result = Estimate(Fan("GPU Fan", 1500, "GpuNvidia"), Fan("CPU Fan", 1500, "GpuNvidia"));

        Assert.Equal(CoolingOutcome.NoData, result.Outcome);
    }

    [Fact]
    public void CpuOptionalFanIsNotTheCpuFan()
    {
        var result = Estimate(Fan("CPU Optional Fan", 1000));

        Assert.Equal(CoolingOutcome.NoData, result.Outcome);
    }

    [Fact]
    public void GenericFanNamesAreReportedButNotTreatedAsCpuFan()
    {
        var result = Estimate(Fan("Fan #1", 1000), Fan("Fan #2", 0));

        Assert.Equal(CoolingOutcome.NoData, result.Outcome);
        Assert.Equal(1, result.UnlabeledRunningFans);
        Assert.Contains(CoolingNote.UnlabeledFansPresent, result.Notes);
    }

    [Fact]
    public void UsbCoolerNameComesThroughAndOthersHaveNone()
    {
        var kraken = new SensorReading("/nzxt", "NZXT Kraken X3", "Cooler", "/nzxt/t", "Liquid", "Temperature", 30, "°C", SensorStatus.Ok, "test", DateTimeOffset.UnixEpoch);

        Assert.Equal("NZXT Kraken X3", Estimate(kraken, Fan("CPU Fan", 1000)).CoolerName);
        Assert.Null(Estimate(Fan("CPU Fan", 1000)).CoolerName);
    }

    [Fact]
    public void LimitedAccessIsNoted()
    {
        var result = CoolingEstimator.Estimate([], isSensorAccessLimited: true);

        Assert.Equal(CoolingOutcome.NoData, result.Outcome);
        Assert.Contains(CoolingNote.SensorAccessLimited, result.Notes);
    }
}
