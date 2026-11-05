using System.Globalization;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Core.Tests;

/// <summary>
/// The tool's own language is a separate choice from the Windows region, so a region must never change what is shown:
/// not decimal commas (de-DE), other calendars (th-TH, ar-SA), other digits or casing rules (tr-TR).
/// </summary>
public class CultureTests
{
    private const char REPLACEMENT_CHARACTER = (char)0xFFFD;

    private static readonly DateTimeOffset At = new(2026, 11, 5, 1, 2, 3, TimeSpan.FromHours(9));

    public static TheoryData<string> Regions => new() { "de-DE", "fr-FR", "th-TH", "ar-SA", "tr-TR", "ja-JP", "fa-IR", "hi-IN", "ko-KR" };

    private static HardwareInventory Hardware() => new(
        new ProbeEnvironment(new OperatingSystemInfo("Windows 11 Pro", "25H2", "1", "X64"), WindowsActivation.Activated, true, true),
        [new CpuInfo("Intel", "Core Ultra 5 225", 10, 10, 3300)],
        [new GpuInfo("Intel", "Arc Pro B60", (ulong)(23.8 * (1UL << 30)), "1", GpuKind.Discrete, "0:1")],
        new MotherboardInfo("ASUS", "B860", "1801", "2025-09-12"),
        new MemoryInfo(48UL << 30, 48UL << 30, [new MemoryModule("A", 24UL << 30, "DDR5", "G Skill Intl", "F5", 8400, 8400, 1400)]),
        [new DiskInfo("0", "CT2000T700", 2_000_398_934_016, "SSD", "NVMe", "Healthy", true, 5, 4)],
        [new DisplayInfo("LG", "GSM", "1", 2020, 3840, 2160, 59.94, 27.2, "DisplayPort", true, "Arc")],
        [],
        new NetworkConnection(NetworkKind.WiFi, "MediaTek", 2882, 85),
        new SystemDriveSpace(600_000_000_000, 2_000_000_000_000),
        Battery: new BatteryInfo(50_000, 41_250, 83),
        Reliability: new ReliabilityInfo(1234, "0x11A", null));

    private static TestRunResult Run() => new(
        TestProfile.Standard,
        At,
        At,
        OverallResult.NoObviousIssue,
        [new PhaseResult(TestPhase.CpuStress, TestStatus.Passed, true, At, At, 120, 120.5, null, 97.5, 99.5, "Sensor", null, 10, 0)],
        [
            new SensorStatistics(TestPhase.CpuStress, "/c", "Cpu", "CPU", "/c/t", "CPU Package", "Temperature", "°C", 40.5, 85.5, 62.25, 60, 60, 60, 1, true),
            new SensorStatistics(TestPhase.CpuStress, "/c", "Cpu", "CPU", "/c/p", "CPU Package", "Power", "W", 20.5, 120.5, 98.25, 60, 60, 60, 1, true),
            new SensorStatistics(TestPhase.CpuStress, "/c", "Cpu", "CPU", "/c/v", "CPU Core", "Voltage", "V", 0.9, 1.25, 1.1, 60, 60, 60, 1, true),
        ],
        [],
        "ArithmeticMeanOfValidSamples",
        60,
        0);

    private static CheckReport Report() => new(CheckReport.CURRENT_SCHEMA_VERSION, "1.0.0", At, Hardware(), [], new CoolingEstimate(CoolingOutcome.LikelyAir, null, 1000, 0, []), Run());

    private static T InCulture<T>(string culture, Func<T> read)
    {
        var (current, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return read();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (current, ui);
        }
    }

    private static string Screen(Translator t)
    {
        // The clock line changes every second by design, so it is compared separately.
        var lines = HardwareSummary.Lines(t, Hardware()).Where(line => line.Label != t["summary.time"]);
        var metrics = LoadMetricSummary.Build(Run()).Select(row => LoadMetricSummary.Text(t, row));
        return string.Join("\n", lines.Select(line => $"{line.Label}={line.Value}").Concat(metrics));
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void ScreenTextDoesNotChangeWithTheWindowsRegion(string region)
    {
        foreach (var language in AppLanguage.Supported)
        {
            var t = Translator.Load(language);
            Assert.Equal(InCulture("en-US", () => Screen(t)), InCulture(region, () => Screen(t)));
        }
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void ReportAndJsonDoNotChangeWithTheWindowsRegion(string region)
    {
        var generator = new HtmlReportGenerator(Translator.Load(AppLanguage.ENGLISH));

        Assert.Equal(InCulture("en-US", () => generator.Generate(Report())), InCulture(region, () => generator.Generate(Report())));
        Assert.Equal(InCulture("en-US", () => JsonReportWriter.Serialize(Report())), InCulture(region, () => JsonReportWriter.Serialize(Report())));
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void ClockStaysGregorianWithWesternDigits(string region)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(9), "(UTC+09:00) Tokyo", "Tokyo");

        Assert.Equal("2026-11-05 01:02:03 (UTC+09:00) Tokyo", InCulture(region, () => HardwareSummary.TimeText(At, zone)));
    }

    [Fact]
    public void FirmwareGarbageIsRemovedRatherThanShown()
    {
        var garbage = $"Vendor{REPLACEMENT_CHARACTER}{(char)7} Model{(char)0}";

        Assert.Equal("Vendor Model", HardwareText.Clean(garbage));
        Assert.Null(HardwareText.Clean($"{REPLACEMENT_CHARACTER}{(char)1}"));
    }

    [Theory]
    [InlineData("Realtek 無線 LAN アダプター")]
    [InlineData("ワイヤレス ネットワーク アダプター")]
    [InlineData("TP-Link 无线网卡")]
    [InlineData("무선 LAN 카드")]
    [InlineData("Intel Dual Band Wi-Fi 6")]
    public void WirelessCardsAreRecognizedInLocalNames(string name)
    {
        Assert.True(NetworkCollector.IsWirelessCard(name));
    }

    [Theory]
    [InlineData("zh-SG", AppLanguage.SIMPLIFIED_CHINESE)]
    [InlineData("zh-HK", AppLanguage.TRADITIONAL_CHINESE)]
    [InlineData("zh-Hant-TW", AppLanguage.TRADITIONAL_CHINESE)]
    [InlineData("ja-JP", AppLanguage.JAPANESE)]
    [InlineData("ko-KR", AppLanguage.ENGLISH)]
    [InlineData("ar-SA", AppLanguage.ENGLISH)]
    [InlineData("", AppLanguage.ENGLISH)]
    public void SystemLanguageMapsToAToolLanguage(string culture, string expected)
    {
        Assert.Equal(expected, AppLanguage.FromCulture(culture));
    }
}
