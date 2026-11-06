using AkikazuHardwareCheck.Core.AdvancedInfo;
using AkikazuHardwareCheck.Core.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Tests;

public class AdvancedInfoTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 1, 0, 0, TimeSpan.FromHours(9));

    private static HardwareInventory Hardware() => new(
        Environment: new ProbeEnvironment(new OperatingSystemInfo("Windows 11 Pro", "26H2", "26300.1", "X64", "2026-05-12"), WindowsActivation.Activated, true, true),
        Cpus: [new CpuInfo("Intel", "Intel(R) Core(TM) Ultra 5 225", 10, 10, 3300, "LGA1851", 22 * 1024, 20 * 1024, "Intel64 Family 6 Model 198 Stepping 2", false)],
        Gpus: [new GpuInfo("Intel", "Intel(R) Arc(TM) Pro B60 Graphics", 24UL << 30, "32.0.101.8737", GpuKind.Discrete, "0:1", "Intel Corporation", "2026-04-29")],
        Motherboard: new MotherboardInfo("ASUSTeK COMPUTER INC.", "ROG STRIX B860-G GAMING WIFI", "1801", "2025-09-12", "American Megatrends", "3.6", "UEFI"),
        Memory: new MemoryInfo(48UL << 30, 48UL << 30, [new MemoryModule("A1", 24UL << 30, "DDR5", "G Skill Intl", "F5-8400J4052G24G", 8400, 8400, 1400)]),
        Disks: [new DiskInfo("0", "CT2000T700SSD3", 2_000_398_934_016, "SSD", "NVMe", "Healthy", true, 5, 4)],
        Displays: [new DisplayInfo("LG Ultra HD", "GSM", "5B7F", 2020, 3840, 2160, 60, 27.2, "DisplayPort", true, "Arc Pro B60", HdrSupported: true, HdrEnabled: true, BitsPerColor: 10, ColorEncoding: "HDR10")],
        Diagnostics: [],
        Network: new NetworkConnection(NetworkKind.WiFi, "MediaTek Wi-Fi 7 MT7925", 2882, 89),
        SystemDrive: new SystemDriveSpace(600_000_000_000, 2_000_000_000_000),
        DiskHealth: [new DiskHealth("0", "CT2000T700SSD3", DiskHealthStatus.Good, 99, 44, 4358, 153, 45, 0, 12, 100, 5, 6_698_941UL * 512_000, 23_920_719UL * 512_000, Findings: [])],
        Peripherals: [new Peripheral(PeripheralKind.Input, "ROG AZOTH", false), new Peripheral(PeripheralKind.Speaker, "LG Ultra HD", false)],
        WiredAdapters: ["Intel Ethernet Controller I226-V"],
        WirelessAdapters: ["MediaTek Wi-Fi 7 MT7925"],
        Computer: new ComputerInfo("ASUS", "ROG STRIX B860-G", false),
        Battery: null,
        Security: new SecurityInfo(true, "2.0"),
        Locale: new LocaleInfo("zh-CN", "zh-CN", "zh-CN", 936),
        Reliability: new ReliabilityInfo(0, "0x11A", Now.AddHours(-9)));

    private static IReadOnlyList<SensorReading> Sensors() =>
    [
        new("/cpu/0", "Intel Core Ultra 5 225", "Cpu", "/cpu/0/temp", "CPU Package", "Temperature", 52, "°C", SensorStatus.Ok, "LibreHardwareMonitorLib", Now),
        new("/gpu/0", "Intel Arc Pro B60", "GpuIntel", "/gpu/0/load", "GPU Core", "Load", 8, "%", SensorStatus.Ok, "LibreHardwareMonitorLib", Now),
    ];

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryLanguageBuildsTheSnapshotWithoutMissingText(string language)
    {
        var snapshot = AdvancedInfoBuilder.Build(Translator.Load(language), Hardware(), Sensors(), Now);

        Assert.Contains(snapshot.Roots, node => node.Key == "overview");
        Assert.Contains(snapshot.Roots, node => node.Key == "cpu");
        Assert.Contains(snapshot.Roots, node => node.Key == "gpu");
        Assert.Contains(snapshot.Roots, node => node.Key == "memory");
        Assert.Contains(snapshot.Roots, node => node.Key == "storage");
        Assert.Contains(snapshot.Roots, node => node.Key == "display");
        Assert.Contains(snapshot.Roots, node => node.Key == "network");
        Assert.All(snapshot.AllFields(), field => Assert.False(string.IsNullOrWhiteSpace(field.Label)));
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryStateAndOriginHasText(string language)
    {
        var t = Translator.Load(language);
        foreach (var state in Enum.GetValues<InfoState>())
        {
            Assert.NotEmpty(t[EnumText.Key("advanced.state", state)]);
        }

        foreach (var origin in Enum.GetValues<InfoOrigin>())
        {
            Assert.NotEmpty(t[EnumText.Key("advanced.origin", origin)]);
        }
    }

    [Fact]
    public void ExistingValuesAreMarkedAvailableAndNotEdited()
    {
        var snapshot = AdvancedInfoBuilder.Build(Translator.Load(AppLanguage.SIMPLIFIED_CHINESE), Hardware(), Sensors(), Now);

        var cpu = snapshot.AllFields().Single(field => field.Key == "cpu.0.model");
        Assert.Equal(InfoState.Available, cpu.State);
        Assert.Contains("Ultra 5 225", cpu.Value);
        Assert.Equal("Intel(R) Core(TM) Ultra 5 225", cpu.RawValue);

        var sensor = snapshot.AllFields().Single(field => field.Key == "sensor./cpu/0/temp");
        Assert.Equal(InfoState.Available, sensor.State);
        Assert.Equal(InfoOrigin.Driver, sensor.Origin);
        Assert.Equal("52 °C", sensor.Value);
    }

    [Fact]
    public void CpuIdAndDisplayHdrArePopulated()
    {
        var snapshot = AdvancedInfoBuilder.Build(Translator.Load(AppLanguage.SIMPLIFIED_CHINESE), Hardware(), Sensors(), Now);

        Assert.Contains(snapshot.AllFields(), field => field.Key == "cpu.cpuid.vendor" && field.State == InfoState.Available);
        Assert.Contains(snapshot.AllFields(), field => field.Key == "display.0.hdrSupported" && field.Value == "有");
        Assert.Contains(snapshot.AllFields(), field => field.Key == "display.0.bitsPerColor" && field.Value == "10");
        Assert.Contains(snapshot.AllFields(), field => field.Key == "storage.volume.0.root");
    }

    [Fact]
    public void ExportContainsProvenanceAndNoHardwareSerials()
    {
        var snapshot = AdvancedInfoBuilder.Build(Translator.Load(AppLanguage.ENGLISH), Hardware(), Sensors(), Now);
        var json = AdvancedInfoExporter.Serialize(snapshot);

        Assert.Contains("\"Roots\"", json);
        Assert.Contains("\"Origin\": \"Driver\"", json);
        Assert.DoesNotContain("SerialNumber", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaintenanceFormatsStayReadable()
    {
        Assert.Equal("2026-05-12", MaintenanceCollectors.FormatInstallDate("20260512"));
        Assert.Equal("", MaintenanceCollectors.FormatInstallDate(null));
        Assert.Equal("16 MB", MaintenanceCollectors.FormatBytes(16L * 1024 * 1024));
    }

    [Fact]
    public void ExportWritesUtf8Json()
    {
        var snapshot = AdvancedInfoBuilder.Build(Translator.Load(AppLanguage.SIMPLIFIED_CHINESE), Hardware(), Sensors(), Now);
        var path = Path.Combine(Path.GetTempPath(), $"advanced-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Null(AdvancedInfoExporter.TrySave(snapshot, path));
            Assert.Contains("计算机概览", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
