using System.Text.RegularExpressions;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Probe;

namespace AkikazuHardwareCheck.Core.Tests;

public partial class LocalizationTests
{
    public static TheoryData<string> Languages => [.. AppLanguage.Supported];

    [Theory]
    [InlineData("ja-JP", AppLanguage.JAPANESE)]
    [InlineData("ja", AppLanguage.JAPANESE)]
    [InlineData("zh-CN", AppLanguage.SIMPLIFIED_CHINESE)]
    [InlineData("zh-SG", AppLanguage.SIMPLIFIED_CHINESE)]
    [InlineData("zh-Hans-HK", AppLanguage.SIMPLIFIED_CHINESE)]
    [InlineData("zh", AppLanguage.SIMPLIFIED_CHINESE)]
    [InlineData("zh-TW", AppLanguage.TRADITIONAL_CHINESE)]
    [InlineData("zh-HK", AppLanguage.TRADITIONAL_CHINESE)]
    [InlineData("zh-MO", AppLanguage.TRADITIONAL_CHINESE)]
    [InlineData("zh-Hant", AppLanguage.TRADITIONAL_CHINESE)]
    [InlineData("en-GB", AppLanguage.ENGLISH)]
    [InlineData("ko-KR", AppLanguage.ENGLISH)]
    [InlineData("", AppLanguage.ENGLISH)]
    public void SystemCultureMapsToSupportedLanguage(string culture, string expected)
    {
        Assert.Equal(expected, AppLanguage.FromCulture(culture));
    }

    [Theory]
    [InlineData("ZH-tw", AppLanguage.TRADITIONAL_CHINESE)]
    [InlineData("fr-FR", null)]
    public void ExplicitLanguageCodesAreValidated(string code, string? expected)
    {
        Assert.Equal(expected, AppLanguage.Parse(code));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguageHasTheSameKeysAndPlaceholders(string language)
    {
        var reference = Translator.Load(AppLanguage.ENGLISH);
        var translator = Translator.Load(language);

        Assert.Equal(reference.Keys.Order(), translator.Keys.Order());
        foreach (var key in reference.Keys)
        {
            Assert.Equal(Placeholders(reference[key]), Placeholders(translator[key]));
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void ReportRendersEveryBranch(string language)
    {
        foreach (var outcome in Enum.GetValues<CoolingOutcome>())
        {
            var rendered = Render(language, FullReport(outcome));

            Assert.Contains(Translator.Load(language)["section.displays"], rendered);
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EmptyReportShowsUnavailableInsteadOfFailing(string language)
    {
        var full = FullReport(CoolingOutcome.NoData);
        var report = full with
        {
            Hardware = full.Hardware with
            {
                Cpus = [],
                Gpus = [],
                Motherboard = null,
                Memory = null,
                Disks = [],
                Displays = [],
            },
            Sensors = [],
        };

        Assert.Contains(Translator.Load(language)["common.unavailable"], Render(language, report));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryActivationStateRenders(string language)
    {
        foreach (var activation in Enum.GetValues<WindowsActivation>())
        {
            var report = FullReport(CoolingOutcome.NoData);
            var rendered = Render(language, report with { Hardware = report.Hardware with { Environment = report.Hardware.Environment with { Activation = activation } } });

            Assert.Contains("Windows", rendered);
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void AccessHintsMatchWhatIsMissing(string language)
    {
        var t = Translator.Load(language);
        var report = FullReport(CoolingOutcome.NoData);

        string RenderAccess(bool isAdministrator, bool isPawnIoInstalled) => Render(language, report with
        {
            Hardware = report.Hardware with
            {
                Environment = report.Hardware.Environment with { IsAdministrator = isAdministrator, IsPawnIoInstalled = isPawnIoInstalled },
            },
        });

        var noDriver = RenderAccess(true, false);
        Assert.Contains(t["environment.needPawnIo"], noDriver);
        Assert.DoesNotContain(t["environment.needAdministrator"], noDriver);

        var noRights = RenderAccess(false, true);
        Assert.Contains(t["environment.needAdministrator"], noRights);
        Assert.DoesNotContain(t["environment.needPawnIo"], noRights);

        var ready = RenderAccess(true, true);
        Assert.DoesNotContain(t["environment.limited"], ready);
    }

    [Fact]
    public void LanguagesProduceDifferentOutput()
    {
        var report = FullReport(CoolingOutcome.LikelyLiquid);

        Assert.Contains("水冷の可能性", Render(AppLanguage.JAPANESE, report));
        Assert.Contains("可能是水冷", Render(AppLanguage.SIMPLIFIED_CHINESE, report));
        Assert.Contains("硬碟", Render(AppLanguage.TRADITIONAL_CHINESE, report));
        Assert.Contains("Possibly liquid cooling", Render(AppLanguage.ENGLISH, report));
    }

    private static string Render(string language, CheckReport report)
    {
        using var output = new StringWriter();
        new ConsoleReport(Translator.Load(language), output).Write(report);
        return output.ToString();
    }

    private static string[] Placeholders(string message) =>
        PlaceholderPattern().Matches(message).Select(match => match.Value).Order().ToArray();

    private static CheckReport FullReport(CoolingOutcome outcome) => new(
        2,
        "0.0.0",
        DateTimeOffset.UnixEpoch,
        new HardwareInventory(
            new ProbeEnvironment(new OperatingSystemInfo("Windows 11 Pro", "25H2", "26200.1", "X64"), WindowsActivation.Activated, false, false),
            [new CpuInfo("AMD", "Ryzen", 8, 16, 4500)],
            [new GpuInfo("NVIDIA", "RTX", 1UL << 33, "32.0", GpuKind.Discrete, "0:1"), new GpuInfo("Microsoft", "Basic", null, null, GpuKind.Software, null)],
            new MotherboardInfo("ASUS", "B650", "1.0", "2026-01-01"),
            new MemoryInfo(1UL << 34, 1UL << 34, [new MemoryModule("A1", 1UL << 34, "DDR5", "Acme", "X1", 5600, 5200)]),
            [new DiskInfo("0", "NVMe SSD", 1_000_000_000_000, "SSD", "NVMe", "Healthy", true)],
            [new DisplayInfo("LG", "GSM", "5B7F", 2024, 2560, 1440, 165, 27, "DisplayPort", true, "RTX"), new DisplayInfo(null, null, null, null, null, null, null, null, null, false, null)],
            [new DiagnosticEntry("Storage", "failed")]),
        [new SensorReading("/hw", "CPU", "Cpu", "/hw/t", "Package", "Temperature", 45, "°C", SensorStatus.Ok, "test", DateTimeOffset.UnixEpoch), new SensorReading("/hw", "CPU", "Cpu", "/hw/b", "Bad", "Temperature", 0, "°C", SensorStatus.Invalid, "test", DateTimeOffset.UnixEpoch)],
        new CoolingEstimate(outcome, 2500, 1000, 2, Enum.GetValues<CoolingNote>()));

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
