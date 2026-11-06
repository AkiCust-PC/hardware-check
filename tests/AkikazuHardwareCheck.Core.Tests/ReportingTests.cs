using System.Globalization;
using System.Text.Json;
using AkikazuHardwareCheck.Core.Configuration;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Core.Tests;

public class ReportingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(9));

    private static HardwareInventory Hardware() => new(
        new ProbeEnvironment(new OperatingSystemInfo("Windows 11 Pro", "25H2", "26200.1", "X64"), WindowsActivation.Activated, true, true),
        [new CpuInfo("AMD", "Ryzen 7 7800X3D", 8, 16, 4200)],
        [new GpuInfo("NVIDIA", "RTX 4070", 12UL << 30, "32.0", GpuKind.Discrete, "0:1"), new GpuInfo("Microsoft", "Basic Render", 0, null, GpuKind.Software, "0:2")],
        new MotherboardInfo("ASUS", "B650", "1.0", "2026-01-01"),
        new MemoryInfo(32UL << 30, 32UL << 30, [new MemoryModule("A1", 16UL << 30, "DDR5", "Acme", "X1", 6000, 5200), new MemoryModule("B1", 16UL << 30, "DDR5", "Acme", "X1", 6000, 5200)]),
        [new DiskInfo("0", "NVMe SSD", 2_000_000_000_000, "SSD", "NVMe", "Healthy", true)],
        [new DisplayInfo("LG", "GSM", "1", 2024, 2560, 1440, 165, 27, "DisplayPort", true, "RTX 4070")],
        []);

    private static SensorStatistics Stat(TestPhase phase, string hardwareType, string hardware, string name, double max, bool isSufficient = true) =>
        new(phase, "/hw", hardwareType, hardware, $"/{hardware}/{name}", name, "Temperature", "°C", max - 10, max, max - 5, 60, 60, 60, 1, isSufficient);

    private static PhaseResult Phase(TestPhase phase, TestStatus status, string? target = null, double seconds = 60) =>
        new(phase, status, true, Now, Now, seconds, seconds, target, 95, 99, "Sensor", null, 10, 0);

    private static TestRunResult Run(OverallResult overall = OverallResult.NoObviousIssue, IReadOnlyList<Anomaly>? anomalies = null) => new(
        TestProfile.Standard,
        Now,
        Now,
        overall,
        [Phase(TestPhase.Idle, TestStatus.Passed), Phase(TestPhase.GpuStress, TestStatus.Passed, "RTX 4070"), Phase(TestPhase.CpuStress, TestStatus.Passed)],
        [
            Stat(TestPhase.Idle, "Cpu", "Ryzen", "Core (Tctl/Tdie)", 45),
            Stat(TestPhase.CpuStress, "Cpu", "Ryzen", "Core (Tctl/Tdie)", 85),
            Stat(TestPhase.Idle, "GpuNvidia", "RTX 4070", "GPU Core", 40),
            Stat(TestPhase.GpuStress, "GpuNvidia", "RTX 4070", "GPU Core", 72),
            Stat(TestPhase.GpuStress, "GpuNvidia", "RTX 4070", "GPU Hot Spot", 99),
        ],
        anomalies ?? [],
        "ArithmeticMeanOfValidSamples",
        300,
        0);

    private static CheckReport Report(TestRunResult? run = null, CoolingOutcome cooling = CoolingOutcome.LikelyLiquid) => new(
        2,
        "1.0.0",
        Now,
        Hardware(),
        [],
        new CoolingEstimate(cooling, 2000, 1000, 0, []),
        run);

    private static string Html(CheckReport report, string language = AppLanguage.JAPANESE) =>
        new HtmlReportGenerator(Translator.Load(language)).Generate(report);

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryLanguageFillsEveryPlaceholder(string language)
    {
        foreach (var report in new[] { Report(), Report(Run()), Report(Run(OverallResult.NeedsReview)) })
        {
            Assert.DoesNotContain("{{", Html(report, language));
        }
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryChoiceHasText(string language)
    {
        var t = Translator.Load(language);
        foreach (var value in Enum.GetValues<WindowsActivation>())
        {
            Assert.NotEmpty(t[EnumText.Key("activation", value)]);
        }

        foreach (var value in Enum.GetValues<CoolingOutcome>())
        {
            Assert.NotEmpty(t[EnumText.Key("cooling", value)]);
        }

        foreach (var value in Enum.GetValues<GpuKind>())
        {
            Assert.NotEmpty(t[EnumText.Key("gpu.kind", value)]);
        }

        foreach (var value in Enum.GetValues<TestStatus>())
        {
            Assert.NotEmpty(t[EnumText.Key("status", value)]);
        }
    }

    [Fact]
    public void UserTextCannotInjectMarkup()
    {
        var html = Html(Report(Run()) with { Hardware = Hardware() with { Motherboard = new MotherboardInfo("ASUS", "<script>alert(1)</script>", "<img src=x onerror=alert(2)>", null) } });

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ConclusionUsesCautiousWordingAndAlwaysShowsTheDisclaimer()
    {
        var t = Translator.Load(AppLanguage.JAPANESE);

        var passed = Html(Report(Run()));
        Assert.Contains(t["report.conclusion.noObviousIssue"], passed);
        Assert.Contains(t["report.disclaimer"], passed);

        Assert.Contains(t["report.conclusion.needsReview"], Html(Report(Run(OverallResult.NeedsReview))));
        Assert.Contains(t["report.conclusion.incomplete"], Html(Report(Run(OverallResult.Incomplete))));

        var notRun = Html(Report());
        Assert.Contains(t["report.conclusion.notRun"], notRun);
        Assert.DoesNotContain(t["report.conclusion.noObviousIssue"], notRun);
        Assert.Contains(t["report.disclaimer"], notRun);
    }

    [Fact]
    public void CoolingIsLabeledAsAnInference()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);

        Assert.Contains(t["report.coolingInferred", t["cooling.likelyLiquid"]], Html(Report(Run()), AppLanguage.SIMPLIFIED_CHINESE));
    }

    [Fact]
    public void NoCoolingDataIsLeftOut()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);

        var html = Html(Report(Run(), CoolingOutcome.NoData), AppLanguage.SIMPLIFIED_CHINESE);

        Assert.DoesNotContain(t["cooling.noData"], html);
        Assert.DoesNotContain(t["report.cooling"], html);
    }

    [Fact]
    public void TwoDrivesKeepTheirOwnLinesInTheReport()
    {
        var hardware = Hardware() with { Disks = [new DiskInfo("0", "AlphaDisk", 1_000_000_000_000, "SSD", "NVMe", "Healthy", true), new DiskInfo("1", "BetaDisk", 1_000_000_000_000, "SSD", "NVMe", "Healthy", false)] };

        var html = Html(Report() with { Hardware = hardware }, AppLanguage.SIMPLIFIED_CHINESE);

        Assert.Matches("AlphaDisk[^<]*<br>[^<]*BetaDisk", html);
    }

    [Fact]
    public void AnomaliesAppearInTheReport()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);
        var run = Run(OverallResult.NeedsReview, [new Anomaly(AnomalyCode.GpuDriverReset, TestPhase.GpuStress, Now, "detail")]);

        Assert.Contains(t["anomaly.gpuDriverReset"], Html(Report(run), AppLanguage.ENGLISH));
    }

    [Fact]
    public void ReportIsATestReportForA4WithoutAcceptanceFields()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        var html = Html(Report(Run()), AppLanguage.SIMPLIFIED_CHINESE);

        Assert.Contains("硬件测试报告", html);
        Assert.Contains("size: A4", html);
        Assert.Contains(t["report.cooling"], html);
        Assert.DoesNotContain("签名", html);
        Assert.DoesNotContain("验收", html);
    }

    [Fact]
    public void ModelsOnlyViewLeavesOutNetworkAdapters()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        var hardware = Hardware() with { WiredAdapters = ["I226-V"], WirelessAdapters = ["MT7925"] };

        var models = HardwareSummary.Lines(t, hardware, isFull: false);
        var full = HardwareSummary.Lines(t, hardware);

        string[] adapters = [t["summary.wiredCard"], t["summary.wirelessCard"]];
        Assert.DoesNotContain(models, line => adapters.Contains(line.Label));
        Assert.All(adapters, label => Assert.Contains(full, line => line.Label == label));
    }

    private static SensorStatistics Metric(TestPhase phase, string hardwareType, string hardware, string name, string type, double average, double maximum) =>
        new(phase, "/hw", hardwareType, hardware, $"/{hardware}/{type}/{name}", name, type, "", average - 1, maximum, average, 60, 60, 60, 1, true);

    [Fact]
    public void LoadMetricsCoverLoadedDevicesOnly()
    {
        var run = Run() with
        {
            Statistics =
            [
                Metric(TestPhase.Idle, "Cpu", "Ryzen", "Package", "Power", 20, 25),
                Metric(TestPhase.DualStress, "Cpu", "Ryzen", "Package", "Power", 150, 160),
                Metric(TestPhase.DualStress, "Cpu", "Ryzen", "Core #1", "Clock", 5000, 5400),
                Metric(TestPhase.DualStress, "Cpu", "Ryzen", "Core #2", "Clock", 4800, 5200),
                Metric(TestPhase.DualStress, "GpuNvidia", "RTX 4070", "GPU Package", "Power", 180, 200),
                Metric(TestPhase.DualStress, "GpuNvidia", "Other Card", "GPU Package", "Power", 300, 350),
            ],
        };

        var rows = LoadMetricSummary.Build(run);

        Assert.Equal(160, rows.Single(row => row.Metric == LoadMetric.CpuPower).Maximum);
        Assert.Equal(200, rows.Single(row => row.Metric == LoadMetric.GpuPower).Maximum);
        Assert.Equal(4900, rows.Single(row => row.Metric == LoadMetric.CpuClock).Average);
        Assert.Equal(5400, rows.Single(row => row.Metric == LoadMetric.CpuClock).Maximum);
        Assert.DoesNotContain(rows, row => row.Metric == LoadMetric.CpuVoltage);

        var unread = Run() with { Statistics = [Metric(TestPhase.DualStress, "Cpu", "Ryzen", "Package", "Power", 0, 0)] };
        Assert.DoesNotContain(LoadMetricSummary.Build(unread), row => row.Metric == LoadMetric.CpuPower);

        var cpuOnly = Run() with { Phases = [Phase(TestPhase.CpuStress, TestStatus.Passed)] };
        Assert.DoesNotContain(LoadMetricSummary.Build(cpuOnly), row => row.Metric == LoadMetric.GpuPower);
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryMetricHasText(string language)
    {
        var t = Translator.Load(language);
        foreach (var value in Enum.GetValues<LoadMetric>())
        {
            Assert.NotEmpty(t[EnumText.Key("metric", value)]);
        }

        Assert.Contains("1.250", LoadMetricSummary.Text(t, new LoadMetricRow(LoadMetric.CpuVoltage, 1.1, 1.25, "V")));
    }

    [Theory]
    [InlineData(new byte[] { 0x1A, 0x01, 0x00, 0x00 }, "0x11A")]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x2F, 0x01, 0x00, 0x00 }, "0x12F")]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 }, null)]
    [InlineData(new byte[] { 0x01 }, null)]
    public void MicrocodeRevisionIsReadFromEitherLayout(byte[] value, string? expected)
    {
        Assert.Equal(expected, ReliabilityCollector.ParseMicrocode(value));
    }

    [Fact]
    public void FullViewOrderAndMergedRows()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        var hardware = Hardware() with { Reliability = new ReliabilityInfo(2, "0x11A", DateTimeOffset.Now.AddHours(-5)), Security = new SecurityInfo(true, "2.0") };

        var full = HardwareSummary.Lines(t, hardware);

        Assert.Equal(t["summary.time"], full[0].Label);
        Assert.Contains("微码 0x11A", full.Single(line => line.Label == t["summary.processor"]).Value);
        Assert.DoesNotContain(full, line => line.Label == t["summary.whea"] || line.Label == t["summary.microcode"] || line.Label == "TPM" || line.Label == t["summary.lastBoot"]);
        Assert.Equal("已开启 TPM 2.0", full.Single(line => line.Label == t["summary.secureBoot"]).Value);
        Assert.Contains("5", HardwareSummary.HistoryLines(t, hardware).Single(line => line.Label == t["summary.systemRecord"]).Value);
        Assert.EndsWith("已激活", full.Single(line => line.Label == t["summary.system"]).Value);

        var models = HardwareSummary.Lines(t, hardware, isFull: false);
        Assert.DoesNotContain(models, line => line.Label == t["summary.time"]);
        Assert.DoesNotContain("微码", models.Single(line => line.Label == t["summary.processor"]).Value);

        string SystemRow(WindowsActivation state) =>
            HardwareSummary.Lines(t, hardware with { Environment = hardware.Environment with { Activation = state } }).Single(line => line.Label == t["summary.system"]).Value;
        Assert.EndsWith("未激活", SystemRow(WindowsActivation.NotActivated));
        Assert.DoesNotContain("激活", SystemRow(WindowsActivation.Unknown));
    }

    [Fact]
    public void BiosDateAndInstallDateAppearInTheFullList()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        var hardware = Hardware() with { Environment = Hardware().Environment with { OperatingSystem = new OperatingSystemInfo("Windows 11", "25H2", "1", "X64", "2026-05-12") } };

        Assert.EndsWith("BIOS 1.0 2026-01-01", HardwareSummary.Lines(t, hardware).Single(line => line.Label == t["section.motherboard"]).Value);
        Assert.DoesNotContain("BIOS", HardwareSummary.Lines(t, hardware, isFull: false).Single(line => line.Label == t["section.motherboard"]).Value);
        Assert.Contains("2026-05-12", HardwareSummary.HistoryLines(t, hardware).Single(line => line.Label == t["summary.systemRecord"]).Value);
    }

    [Fact]
    public void LanguageEnvironmentIncludesCodePage()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);

        var row = HardwareSummary.Lines(t, Hardware() with { Locale = new LocaleInfo("zh-CN", "ja-JP", "ja-JP", 932) }).Single(line => line.Label == t["summary.languageEnvironment"]).Value;
        Assert.Contains("Shift-JIS", row);
    }

    [Fact]
    public void ReportShowsWheaWithTheTestResultsAndFlagsErrors()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);
        var clean = Report(Run()) with { Hardware = Hardware() with { Reliability = new ReliabilityInfo(0, null, null) } };
        var failing = Report(Run()) with { Hardware = Hardware() with { Reliability = new ReliabilityInfo(4, null, null) } };

        Assert.Contains(t["summary.whea"], Html(clean, AppLanguage.ENGLISH));
        Assert.DoesNotContain($"class=\"alert\">{t["summary.wheaValue", 30, 0]}", Html(clean, AppLanguage.ENGLISH));
        Assert.Contains($"class=\"alert\">{t["summary.wheaValue", 30, 4]}", Html(failing, AppLanguage.ENGLISH));
        Assert.DoesNotContain(t["summary.whea"], Html(Report(Run()), AppLanguage.ENGLISH));
    }

    [Fact]
    public void DriverRowShowsWhatWasRead()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        GpuInfo card = new("NVIDIA", "RTX 4070", 12UL << 30, "32.0", GpuKind.Discrete, "0:1", "NVIDIA", "2026-04-29");

        Assert.Equal("NVIDIA 32.0 2026-04-29", HardwareSummary.Lines(t, Hardware() with { Gpus = [card] }).Single(line => line.Label == t["summary.gpuDriver"]).Value);
        Assert.Equal("32.0", HardwareSummary.Lines(t, Hardware() with { Gpus = [card with { DriverProvider = null, DriverDate = null }] }).Single(line => line.Label == t["summary.gpuDriver"]).Value);
        Assert.DoesNotContain(HardwareSummary.Lines(t, Hardware() with { Gpus = [card with { DriverProvider = null, DriverDate = null, DriverVersion = null }] }), line => line.Label == t["summary.gpuDriver"]);
    }

    [Fact]
    public void CpuWindowListsWhatWasReadAndWhea()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        var hardware = Hardware() with
        {
            Cpus = [new CpuInfo("Intel", "Core Ultra 5 225", 10, 10, 3300, "LGA1851", 22528, 20480, "Intel64 Family 6", false)],
            Reliability = new ReliabilityInfo(3, "0x11A", null),
        };

        var lines = CpuSummary.Details(t, hardware);

        Assert.Equal("LGA1851", lines.Single(line => line.Label == t["cpu.socket"]).Value);
        Assert.Equal("22 MB", lines.Single(line => line.Label == t["cpu.cacheL2"]).Value);
        Assert.Equal("未开启", lines.Single(line => line.Label == t["cpu.virtualization"]).Value);
        Assert.Contains("3", lines.Single(line => line.Label == t["summary.whea"]).Value);

        var sparse = CpuSummary.Details(t, Hardware() with { Cpus = [new CpuInfo(null, "X", null, null, null)], Reliability = null });
        Assert.Equal([t["summary.processor"]], sparse.Select(line => line.Label));
    }

    [Fact]
    public void AnythingUnreadIsLeftOutOfTheList()
    {
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        var hardware = Hardware() with { Motherboard = null, Memory = null, Disks = [], Network = null, Security = null };

        var labels = HardwareSummary.Lines(t, hardware).Select(line => line.Label).ToList();

        Assert.DoesNotContain(HardwareSummary.Lines(t, hardware), line => line.Value == t["common.unavailable"]);
        Assert.DoesNotContain(t["section.motherboard"], labels);
        Assert.DoesNotContain(t["section.memory"], labels);
        Assert.DoesNotContain(t["section.disks"], labels);
        Assert.DoesNotContain(t["summary.network"], labels);
        Assert.DoesNotContain(t["summary.secureBoot"], labels);
    }

    [Fact]
    public void FullViewShowsLanguageAndTimeWithZone()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);
        var hardware = Hardware() with { Locale = new LocaleInfo("zh-CN", "ja-JP", "ja-JP", 932) };

        var full = HardwareSummary.Lines(t, hardware);

        Assert.Contains(CultureInfo.GetCultureInfo("ja-JP").NativeName, full.Single(line => line.Label == t["summary.languageEnvironment"]).Value);
        var same = HardwareSummary.Lines(t, hardware with { Locale = new LocaleInfo("ja-JP", "ja-JP", "ja-JP", 932) });
        Assert.DoesNotContain("Format", same.Single(line => line.Label == t["summary.languageEnvironment"]).Value);
        Assert.Contains(full, line => line.Label == t["summary.time"]);
        Assert.DoesNotContain(HardwareSummary.Lines(t, hardware, isFull: false), line => line.Label == t["summary.time"]);

        var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(9), "(UTC+09:00) Tokyo", "Tokyo");
        Assert.Equal("2026-11-05 01:02:03 (UTC+09:00) Tokyo", HardwareSummary.TimeText(new DateTimeOffset(2026, 11, 5, 1, 2, 3, TimeSpan.FromHours(9)), zone));
    }

    [Fact]
    public void DualTestSamplesStandForEachDeviceUnderLoad()
    {
        var run = Run() with
        {
            Statistics = [Stat(TestPhase.DualStress, "Cpu", "Ryzen", "Core (Tctl/Tdie)", 85), Stat(TestPhase.DualStress, "GpuNvidia", "RTX 4070", "GPU Core", 72)],
        };

        var rows = TemperatureSummary.Build(run, Hardware().Gpus[0]);

        Assert.Equal(85, rows.Single(row => row is { IsCpu: true, Phase: TestPhase.CpuStress }).Maximum);
        Assert.Equal(72, rows.Single(row => row is { IsCpu: false, Phase: TestPhase.GpuStress }).Maximum);
    }

    [Fact]
    public void SummaryRowsUseTheSameSensorAcrossPhasesAndExcludeHotSpot()
    {
        var rows = TemperatureSummary.Build(Run(), Hardware().Gpus[0]);

        Assert.Equal(4, rows.Count);
        Assert.Equal(85, rows.Single(row => row is { IsCpu: true, Phase: TestPhase.CpuStress }).Maximum);
        Assert.Equal(72, rows.Single(row => row is { IsCpu: false, Phase: TestPhase.GpuStress }).Maximum);
        Assert.All(rows, row => Assert.Equal(TemperatureRowState.Measured, row.State));
    }

    [Fact]
    public void MissingCpuTemperatureIsUnavailableNotZero()
    {
        var run = Run() with { Statistics = [Stat(TestPhase.GpuStress, "GpuNvidia", "RTX 4070", "GPU Core", 72)] };

        var row = TemperatureSummary.Build(run, Hardware().Gpus[0]).Single(item => item is { IsCpu: true, Phase: TestPhase.CpuStress });

        Assert.Equal(TemperatureRowState.Unavailable, row.State);
        Assert.Null(row.Maximum);
    }

    [Fact]
    public void TooFewSamplesAreMarkedInsufficient()
    {
        var run = Run() with { Statistics = [Stat(TestPhase.CpuStress, "Cpu", "Ryzen", "Core (Tctl/Tdie)", 85, isSufficient: false)] };

        var row = TemperatureSummary.Build(run, null).Single(item => item is { IsCpu: true, Phase: TestPhase.CpuStress });

        Assert.Equal(TemperatureRowState.InsufficientData, row.State);
    }

    [Fact]
    public void PhaseThatDidNotRunIsNotRun()
    {
        var run = Run() with { Phases = [Phase(TestPhase.CpuStress, TestStatus.NotRun, seconds: 0)] };

        var row = TemperatureSummary.Build(run, null).Single(item => item is { IsCpu: true, Phase: TestPhase.CpuStress });

        Assert.Equal(TemperatureRowState.NotRun, row.State);
    }

    [Fact]
    public void JsonCarriesTemperatureRowsForTheWebsiteImport()
    {
        using var document = JsonDocument.Parse(JsonReportWriter.Serialize(Report(Run())));

        var rows = document.RootElement.GetProperty("Temperatures");
        Assert.Equal(4, rows.GetArrayLength());
        Assert.Contains(rows.EnumerateArray(), row => row.GetProperty("Phase").GetString() == "CpuStress" && row.GetProperty("Maximum").GetDouble() == 85);
        Assert.False(document.RootElement.TryGetProperty("Acceptance", out _));
    }

    [Fact]
    public void SavingWritesBothFilesUnderOneName()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, $"test-output-{Guid.NewGuid():N}");
        try
        {
            var saved = ReportStore.Save(Report(Run()), folder, Translator.Load(AppLanguage.JAPANESE));

            Assert.EndsWith("hardware-check-20261104-120000.html", saved.HtmlPath);
            Assert.EndsWith("hardware-check-20261104-120000.json", saved.JsonPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void HardwareSummaryListsEveryDeviceAndHidesSoftwareRenderer()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);
        var text = HardwareSummary.AsText(HardwareSummary.Lines(t, Hardware()));

        Assert.Contains("Ryzen 7 7800X3D", text);
        Assert.Contains("RTX 4070", text);
        Assert.DoesNotContain("Basic Render", text);
        Assert.Contains("5200MT/s", text);
        Assert.Contains("2TB", text);
    }

    [Fact]
    public void SettingsRoundTripAndDamagedFileGivesDefaults()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"test-output-{Guid.NewGuid():N}", "settings.json");
        try
        {
            Assert.Null(new AppSettings(Language: AppLanguage.JAPANESE, CpuProtectionCelsius: 90).TrySave(path));
            var loaded = AppSettings.Load(path);
            Assert.Equal(AppLanguage.JAPANESE, loaded.Language);
            Assert.Equal(90, loaded.ToProfile().CpuProtectionCelsius);

            File.WriteAllText(path, "{ not json");
            Assert.Null(AppSettings.Load(path).Language);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void MissingFloatingFlagDefaultsOn()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"test-output-{Guid.NewGuid():N}", "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"Language\":\"zh-CN\"}");
            Assert.True(AppSettings.Load(path).FloatingEnabled);

            File.WriteAllText(path, "{\"FloatingEnabled\":false}");
            Assert.False(AppSettings.Load(path).FloatingEnabled);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
        }
    }

    [Fact]
    public void ProfileFromSettingsStaysInTheSupportedRange()
    {
        var profile = new AppSettings(GpuStressSeconds: 5, CpuStressSeconds: 1000).ToProfile();

        Assert.Equal(60, profile.GpuStressSeconds);
        Assert.Equal(300, profile.CpuStressSeconds);
    }
}
