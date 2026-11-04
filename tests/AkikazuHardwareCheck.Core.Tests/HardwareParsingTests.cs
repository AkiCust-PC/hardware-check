using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace AkikazuHardwareCheck.Core.Tests;

public class HardwareParsingTests
{
    [Theory]
    [InlineData(@"\\?\DISPLAY#GSM5B7F#5&1a2b3c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}")]
    [InlineData(@"DISPLAY\GSM5B7F\5&1a2b3c&0&UID4352_0")]
    public void MonitorPathAndWmiInstanceShareOneKey(string input)
    {
        Assert.Equal(@"DISPLAY\GSM5B7F\5&1A2B3C&0&UID4352", HardwareText.MonitorKey(input));
    }

    [Fact]
    public void MonitorIdsComeFromTheKey()
    {
        Assert.Equal(("GSM", "5B7F"), HardwareText.ParseMonitorIds(@"DISPLAY\GSM5B7F\5&1A2B3C"));
        Assert.Equal(((string?)null, (string?)null), HardwareText.ParseMonitorIds(null));
        Assert.Equal(((string?)null, (string?)null), HardwareText.ParseMonitorIds(@"DISPLAY\Default_Monitor\1"));
    }

    [Fact]
    public void CodeUnitsStopAtFirstNull()
    {
        ushort[] name = ['L', 'G', ' ', 'U', 'W', 0, 0, 'x'];

        Assert.Equal("LG UW", HardwareText.DecodeCodeUnits(name));
        Assert.Null(HardwareText.DecodeCodeUnits([0, 0]));
        Assert.Null(HardwareText.DecodeCodeUnits(null));
    }

    [Theory]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("  default string ")]
    [InlineData("")]
    [InlineData(null)]
    public void FirmwarePlaceholdersBecomeUnavailable(string? value)
    {
        Assert.Null(HardwareText.Clean(value));
    }

    [Fact]
    public void RealValuesSurviveCleaning()
    {
        Assert.Equal("ASUSTeK COMPUTER INC.", HardwareText.Clean(" ASUSTeK COMPUTER INC. "));
    }

    [Theory]
    [InlineData(60, 34, 27.2)]
    [InlineData(0, 0, null)]
    [InlineData(null, 34, null)]
    public void DiagonalNeedsBothPhysicalDimensions(int? widthCm, int? heightCm, double? expected)
    {
        Assert.Equal(expected, DisplayInfoCollector.DiagonalInches(widthCm, heightCm));
    }

    [Theory]
    [InlineData(5u, "HDMI")]
    [InlineData(10u, "DisplayPort")]
    [InlineData(0x80000000u, "Internal")]
    [InlineData(999u, null)]
    public void ConnectionTypesAreMapped(uint technology, string? expected)
    {
        Assert.Equal(expected, DisplayInfoCollector.MapConnection(technology));
    }

    [Theory]
    [InlineData(34, "DDR5")]
    [InlineData(26, "DDR4")]
    [InlineData(0, null)]
    public void MemoryTypesAreMapped(int smbiosType, string? expected)
    {
        Assert.Equal(expected, SystemInfoCollector.MapMemoryType(smbiosType));
    }

    [Theory]
    [InlineData(SensorType.Temperature, 45d, SensorStatus.Ok)]
    [InlineData(SensorType.Temperature, 0d, SensorStatus.Invalid)]
    [InlineData(SensorType.Temperature, 255d, SensorStatus.Invalid)]
    [InlineData(SensorType.Fan, 0d, SensorStatus.Ok)]
    [InlineData(SensorType.Fan, 65535d, SensorStatus.Invalid)]
    [InlineData(SensorType.Fan, null, SensorStatus.NoValue)]
    [InlineData(SensorType.Load, double.NaN, SensorStatus.Invalid)]
    public void SensorValuesAreClassified(SensorType type, double? value, SensorStatus expected)
    {
        Assert.Equal(expected, LhmSensorSource.Classify(type, value));
    }
}
