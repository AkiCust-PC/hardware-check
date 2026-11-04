using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Core.Tests;

public class DiskHealthTests
{
    private static byte[] Log(byte warning = 0, byte used = 1, ulong mediaErrors = 0)
    {
        var log = new byte[512];
        log[0] = warning;
        BitConverter.TryWriteBytes(log.AsSpan(1), (ushort)318); // 45 °C
        log[3] = 100;
        log[4] = 5;
        log[5] = used;
        BitConverter.TryWriteBytes(log.AsSpan(32), 6698941UL);
        BitConverter.TryWriteBytes(log.AsSpan(48), 23920719UL);
        BitConverter.TryWriteBytes(log.AsSpan(112), 153UL);
        BitConverter.TryWriteBytes(log.AsSpan(128), 4357UL);
        BitConverter.TryWriteBytes(log.AsSpan(144), 45UL);
        BitConverter.TryWriteBytes(log.AsSpan(160), mediaErrors);
        return log;
    }

    [Fact]
    public void NvmeLogFieldsComeFromTheirSpecifiedOffsets()
    {
        var health = DiskHealthCollector.ParseNvmeLog("0", "Disk", Log());

        Assert.Equal(DiskHealthStatus.Good, health.Status);
        Assert.Equal(99, health.LifePercent);
        Assert.Equal(45, health.TemperatureCelsius);
        Assert.Equal(100, health.AvailableSparePercent);
        Assert.Equal(5, health.SpareThresholdPercent);
        Assert.Equal(4357UL, health.PowerOnHours);
        Assert.Equal(153UL, health.PowerCycles);
        Assert.Equal(45UL, health.UnsafeShutdowns);
        Assert.Equal(0UL, health.MediaErrors);
        Assert.Equal(23920719UL * 512000, health.DataWrittenBytes);
        Assert.Equal(6698941UL * 512000, health.DataReadBytes);
    }

    [Fact]
    public void LifeNeverGoesBelowZeroWhenADriveIsPastItsRating()
    {
        Assert.Equal(0, DiskHealthCollector.ParseNvmeLog("0", "Disk", Log(used: 180)).LifePercent);
    }

    [Theory]
    [InlineData(0b0000_0001, DiskHealthStatus.Bad, DiskFinding.SpareBelowThreshold)]
    [InlineData(0b0000_0100, DiskHealthStatus.Bad, DiskFinding.ReliabilityDegraded)]
    [InlineData(0b0000_1000, DiskHealthStatus.Bad, DiskFinding.ReadOnlyMode)]
    [InlineData(0b0000_0010, DiskHealthStatus.Caution, DiskFinding.OverTemperature)]
    [InlineData(0b0001_0000, DiskHealthStatus.Caution, DiskFinding.BackupFailed)]
    public void CriticalWarningBitsDecideTheStatus(byte warning, DiskHealthStatus status, DiskFinding finding)
    {
        var (actual, findings) = DiskHealthCollector.JudgeNvme(warning, 0, 99);

        Assert.Equal(status, actual);
        Assert.Contains(finding, findings);
    }

    [Fact]
    public void MediaErrorsAreACaution()
    {
        var (status, findings) = DiskHealthCollector.JudgeNvme(0, 1, 99);

        Assert.Equal(DiskHealthStatus.Caution, status);
        Assert.Contains(DiskFinding.MediaErrors, findings);
    }

    [Theory]
    [InlineData(10, DiskHealthStatus.Caution)]
    [InlineData(11, DiskHealthStatus.Good)]
    public void LowLifeFollowsTheTenPercentRule(int life, DiskHealthStatus expected)
    {
        Assert.Equal(expected, DiskHealthCollector.JudgeNvme(0, 0, life).Status);
    }

    [Theory]
    [InlineData(1UL, 0UL, 0UL, DiskFinding.ReallocatedSectors)]
    [InlineData(0UL, 1UL, 0UL, DiskFinding.PendingSectors)]
    [InlineData(0UL, 0UL, 1UL, DiskFinding.UncorrectableSectors)]
    public void AnyKeyAtaRawValueIsACaution(ulong reallocated, ulong pending, ulong uncorrectable, DiskFinding finding)
    {
        var (status, findings) = DiskHealthCollector.JudgeAta(reallocated, pending, uncorrectable, null, false);

        Assert.Equal(DiskHealthStatus.Caution, status);
        Assert.Contains(finding, findings);
    }

    [Fact]
    public void AtaWithCleanAttributesIsGoodAndThresholdFailureIsBad()
    {
        Assert.Equal(DiskHealthStatus.Good, DiskHealthCollector.JudgeAta(0, 0, 0, 90, false).Status);
        Assert.Equal(DiskHealthStatus.Bad, DiskHealthCollector.JudgeAta(0, 0, 0, 90, true).Status);
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryStatusAndFindingHasText(string language)
    {
        var t = Translator.Load(language);
        foreach (var status in Enum.GetValues<DiskHealthStatus>())
        {
            Assert.NotEmpty(t[EnumText.Key("diskHealth", status)]);
        }

        foreach (var finding in Enum.GetValues<DiskFinding>())
        {
            Assert.NotEmpty(t[EnumText.Key("diskFinding", finding)]);
        }

        foreach (var kind in Enum.GetValues<PeripheralKind>())
        {
            Assert.NotEmpty(t[EnumText.Key("peripheral", kind)]);
        }
    }

    [Fact]
    public void HeadlineLeadsWithStatusLifeAndTemperature()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);

        var line = DiskHealthSummary.Headline(t, DiskHealthCollector.ParseNvmeLog("0", "Disk", Log()));

        Assert.StartsWith("良好 99% 45°C 通电", line);
        Assert.Contains("写入", line);
        Assert.EndsWith("介质错误0", line);
    }

    [Fact]
    public void DetailsListOnlyWhatTheDriveProvided()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);

        var details = DiskHealthSummary.Details(t, new DiskHealth("1", "Disk", DiskHealthStatus.Unknown, Findings: [DiskFinding.NeedsAdministrator]));

        Assert.Equal(2, details.Count);
        Assert.Equal(t["diskFinding.needsAdministrator"], details[1].Value);
    }

    [Fact]
    public void GenericInputNamesAreHiddenWhenARealNameExists()
    {
        Peripheral?[] raw =
        [
            PeripheralCollector.Classify("HID Keyboard Device", "Keyboard", @"HID\VID_1"),
            PeripheralCollector.Classify("ROG AZOTH EXTREME", "Mouse", @"HID\VID_2"),
            PeripheralCollector.Classify("ROG AZOTH EXTREME", "Keyboard", @"HID\VID_3"),
        ];

        var result = PeripheralCollector.Normalize(raw.OfType<Peripheral>());

        Assert.Equal("ROG AZOTH EXTREME", Assert.Single(result).Name);
    }

    [Fact]
    public void OnlyGenericInputStillShowsThatADeviceIsConnected()
    {
        var result = PeripheralCollector.Normalize([PeripheralCollector.Classify("HID-compliant mouse", "Mouse", @"HID\VID_1")!]);

        Assert.Null(Assert.Single(result).Name);
    }

    [Theory]
    [InlineData("Canon MF660C Series (2b:21:55)", "Canon MF660C Series")]
    [InlineData("Realtek Digital Output (Realtek(R) Audio)", "Realtek Digital Output (Realtek Audio)")]
    [InlineData("LG Ultra HD (HD Audio Driver for Display Audio)", "LG Ultra HD")]
    public void DeviceNamesLoseAddressFragmentsAndMarks(string raw, string expected)
    {
        Assert.Equal(expected, PeripheralCollector.CleanName(raw));
    }

    [Fact]
    public void NetworkRateIgnoresCounterResets()
    {
        Assert.Null(ActivityMonitor.RatePerSecond(null, 100, 1));
        Assert.Equal(50, ActivityMonitor.RatePerSecond(100, 200, 2));
        Assert.Equal(0, ActivityMonitor.RatePerSecond(500, 100, 1));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(25, 75)]
    [InlineData(130, 0)]
    public void DiskBusyIsTheInverseOfIdle(double idle, double expected)
    {
        Assert.Equal(expected, ActivityMonitor.BusyFromIdle(idle));
    }
}
