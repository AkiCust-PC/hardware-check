using System.Text.Json;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Core.Tests;

public class JsonReportWriterTests
{
    private static CheckReport Report() => new(
        2,
        "0.0.0",
        DateTimeOffset.UnixEpoch,
        new HardwareInventory(
            new ProbeEnvironment(new OperatingSystemInfo("Windows 11 Pro", "25H2", "26200.1", "X64"), WindowsActivation.Activated, true, true),
            [],
            [new GpuInfo("Intel", "Arc", null, null, GpuKind.Discrete, "0:1")],
            null,
            null,
            [],
            [],
            []),
        [new SensorReading("/hw", "CPU", "Cpu", "/hw/t", "Package", "Temperature", null, "°C", SensorStatus.NoValue, "test", DateTimeOffset.UnixEpoch)],
        new CoolingEstimate(CoolingOutcome.LikelyAir, null, 1000, 0, [CoolingNote.PumpMayBeUnreported]));

    [Fact]
    public void SavingToADirectoryReportsFailureInsteadOfThrowing()
    {
        var failure = JsonReportWriter.TrySave(Report(), Path.GetTempPath());

        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Fact]
    public void JsonUsesLanguageNeutralCodes()
    {
        using var document = JsonDocument.Parse(JsonReportWriter.Serialize(Report()));
        var root = document.RootElement;

        Assert.Equal("LikelyAir", root.GetProperty("Cooling").GetProperty("Outcome").GetString());
        Assert.Equal("PumpMayBeUnreported", root.GetProperty("Cooling").GetProperty("Notes")[0].GetString());
        Assert.Equal("NoValue", root.GetProperty("Sensors")[0].GetProperty("Status").GetString());
        Assert.Equal("°C", root.GetProperty("Sensors")[0].GetProperty("Unit").GetString());
        Assert.Equal("Activated", root.GetProperty("Hardware").GetProperty("Environment").GetProperty("Activation").GetString());
        Assert.Equal("Discrete", root.GetProperty("Hardware").GetProperty("Gpus")[0].GetProperty("Kind").GetString());
    }
}
