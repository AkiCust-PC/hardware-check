using System.Net.NetworkInformation;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.Core.Tests;

public class DisplayNamesTests
{
    private static readonly Translator Chinese = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);

    private static HardwareInventory Hardware(MemoryInfo? memory = null, IReadOnlyList<GpuInfo>? gpus = null) => new(
        new ProbeEnvironment(new OperatingSystemInfo("Windows 11 Pro", "25H2", "1", "X64"), WindowsActivation.Activated, true, true),
        [new CpuInfo("Intel", "Intel(R) Core(TM) Ultra 5 225", 10, 10, 3300)],
        gpus ?? [new GpuInfo("Intel", "Intel(R) Arc(TM) Pro B60 Graphics", (ulong)(23.8 * (1UL << 30)), "1", GpuKind.Discrete, "0:1"), new GpuInfo("Intel", "Intel(R) Graphics", 128UL << 20, "1", GpuKind.Integrated, "0:2")],
        new MotherboardInfo("ASUSTeK COMPUTER INC.", "ROG STRIX B860-G GAMING WIFI", "1801", "2025-09-12"),
        memory ?? new MemoryInfo(48UL << 30, 48UL << 30, [new MemoryModule("A", 24UL << 30, "DDR5", "G Skill Intl", "F5-8400J4052G24G", 8400, 8400, 1400), new MemoryModule("B", 24UL << 30, "DDR5", "G Skill Intl", "F5-8400J4052G24G", 8400, 8400, 1400)]),
        [new DiskInfo("0", "CT2000T700SSD3", 2_000_398_934_016, "SSD", "NVMe", "Healthy", true, 5, 4)],
        [new DisplayInfo("LG Ultra HD", "GSM", "1", 2020, 3840, 2160, 60, 27.2, "DisplayPort", true, "Intel(R) Arc(TM) Pro B60 Graphics")],
        [],
        new NetworkConnection(NetworkKind.WiFi, "MediaTek", 2882),
        new SystemDriveSpace(600_000_000_000, 2_000_000_000_000),
        null,
        [new Peripheral(PeripheralKind.Input, "ROG AZOTH", false), new Peripheral(PeripheralKind.Input, "ROG HARPE", false), new Peripheral(PeripheralKind.Speaker, "LG Ultra HD", false)],
        ["Intel Ethernet Controller I226-V"],
        ["MediaTek Wi-Fi 7 MT7925"]);

    private static string Value(HardwareInventory hardware, string label) =>
        HardwareSummary.Lines(Chinese, hardware).First(line => line.Label == label).Value;

    [Fact]
    public void RequestedChineseFormatHasNoSeparators()
    {
        var lines = HardwareSummary.Lines(Chinese, Hardware());

        Assert.Equal("Intel Core Ultra 5 225 10核 10线程 含核显", Value(Hardware(), "处理器"));
        Assert.Contains(lines, line => line is { Label: "显卡", Value: "Intel Arc Pro B60 23.8GB显存" });
        Assert.Single(lines, line => line.Label == "显卡");
        Assert.Equal("ASUS ROG STRIX B860-G GAMING WIFI BIOS 1801 2025-09-12", Value(Hardware(), "主板"));
        Assert.Equal("G.Skill DDR5 48GB (24GB × 2) 8400MT/s F5-8400J4052G24G", Value(Hardware(), "内存"));
        Assert.Equal("Crucial CT2000T700SSD3 2TB NVMe SSD PCIe 5.0 x4", Value(Hardware(), "硬盘"));
        Assert.Equal("LG Ultra HD 3840 × 2160 60Hz 27.2英寸 独立显卡 DisplayPort", Value(Hardware(), "显示器"));
        Assert.Equal("Wi-Fi 连接 2.9Gbps", Value(Hardware(), "网络"));
        Assert.Equal("已用 600GB / 共 2TB", Value(Hardware(), "系统盘空间"));
        Assert.All(lines, line => Assert.DoesNotContain("|", line.Value));
    }

    [Fact]
    public void ModelsOnlyViewKeepsJustHardwareModelsAndParameters()
    {
        var lines = HardwareSummary.Lines(Chinese, Hardware(), isFull: false);

        Assert.Equal(["处理器", "显卡", "主板", "内存", "硬盘", "显示器"], lines.Select(line => line.Label));
        Assert.Equal("ASUS ROG STRIX B860-G GAMING WIFI", lines.Single(line => line.Label == "主板").Value);
    }

    [Fact]
    public void FullViewAddsStatusNetworkAndSystem()
    {
        var labels = HardwareSummary.Lines(Chinese, Hardware(), isFull: true).Select(line => line.Label).ToList();

        Assert.Contains("网络", labels);
        Assert.Contains("系统", labels);
        Assert.Contains("系统盘空间", labels);
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryLanguageRendersTheSummaryWithoutSeparators(string language)
    {
        var lines = HardwareSummary.Lines(Translator.Load(language), Hardware());

        Assert.All(lines, line => Assert.DoesNotContain("|", line.Value));
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryNetworkKindHasText(string language)
    {
        var t = Translator.Load(language);
        foreach (var kind in Enum.GetValues<NetworkKind>())
        {
            Assert.NotEmpty(t[EnumText.Key("network", kind)]);
        }
    }

    [Fact]
    public void MixedModulesListEachSize()
    {
        var memory = new MemoryInfo(null, 48UL << 30, [new MemoryModule("A", 32UL << 30, "DDR5", null, null, 5600, 5600), new MemoryModule("B", 16UL << 30, "DDR5", null, null, 5600, 5600)]);

        Assert.Equal("DDR5 48GB (32GB + 16GB) 5600MT/s", Value(Hardware(memory), "内存"));
    }

    [Fact]
    public void CpuWithoutIntegratedGpuHasNoSuffix()
    {
        GpuInfo[] gpus = [new("NVIDIA", "RTX 4070", 12UL << 30, "1", GpuKind.Discrete, "0:1")];

        Assert.Equal("Intel Core Ultra 5 225 10核 10线程", Value(Hardware(gpus: gpus), "处理器"));
    }

    [Fact]
    public void SpecificIntegratedGpuNameIsKept()
    {
        GpuInfo[] gpus = [new("AMD", "AMD Radeon 780M Graphics", 2UL << 30, "1", GpuKind.Integrated, "0:1")];

        Assert.EndsWith("含核显 AMD Radeon 780M", Value(Hardware(gpus: gpus), "处理器"));
        Assert.Equal("无独立显卡", Value(Hardware(gpus: gpus), "显卡"));
    }

    [Theory]
    [InlineData("G Skill Intl", "G.Skill")]
    [InlineData("859B", null)]
    [InlineData("Unknown", null)]
    [InlineData(null, null)]
    [InlineData("Acme Memory", "Acme Memory")]
    public void MemoryBrandsAreReadableOrHidden(string? raw, string? expected)
    {
        Assert.Equal(expected, DisplayNames.MemoryBrand(raw));
    }

    [Fact]
    public void DevicesOfOneKindShareARowAndMissingKindsHaveNone()
    {
        var lines = HardwareSummary.PeripheralLines(Chinese, Hardware().Peripherals);

        Assert.Equal($"ROG AZOTH{Environment.NewLine}ROG HARPE", lines.Single(line => line.Label == "键鼠").Value);
        Assert.Equal("LG Ultra HD", lines.Single(line => line.Label == "扬声器").Value);
        Assert.DoesNotContain(lines, line => line.Label is "麦克风" or "摄像头" or "打印机");
    }

    [Theory]
    [InlineData("ASUS", "ASUS TUF Dash F15 FX517ZR FX517ZR", "ASUS TUF Dash F15 FX517ZR")]
    [InlineData("ASUS", "ASUS TUF Dash F15 FX517ZR_FX517ZR", "ASUS TUF Dash F15 FX517ZR")]
    [InlineData("Dell", "OptiPlex 7010", "Dell OptiPlex 7010")]
    [InlineData(null, "ThinkPad", "ThinkPad")]
    public void ComputerNamesKeepEachWordOnce(string? brand, string model, string expected)
    {
        Assert.Equal(expected, DisplayNames.ComputerName(brand, model));
    }

    [Fact]
    public void ComputerRowSaysWhatKindOfComputerItIs()
    {
        var laptop = HardwareSummary.Lines(Chinese, Hardware() with { Computer = new ComputerInfo("ASUS", "ASUS TUF Dash F15 FX517ZR FX517ZR", true) });
        var desktop = HardwareSummary.Lines(Chinese, Hardware() with { Computer = new ComputerInfo("Dell", "OptiPlex 7010", false) });
        var unnamed = HardwareSummary.Lines(Chinese, Hardware() with { Computer = new ComputerInfo(null, null, true) });

        Assert.Equal("ASUS TUF Dash F15 FX517ZR", laptop.Single(line => line.Label == "笔记本电脑").Value);
        Assert.Equal("Dell OptiPlex 7010", desktop.Single(line => line.Label == "台式电脑").Value);
        Assert.DoesNotContain(unnamed, line => line.Label == "笔记本电脑");
    }

    [Fact]
    public void BuiltInDisplayWithoutANameReadsNaturally()
    {
        var screen = new DisplayInfo(null, null, null, null, 1920, 1080, 60, 15.3, "Internal", true, null);

        var value = HardwareSummary.Lines(Chinese, Hardware() with { Displays = [screen] }).Single(line => line.Label == "显示器").Value;

        Assert.Equal("1920 × 1080 60Hz 15.3英寸 内置屏幕", value);
    }

    [Fact]
    public void NothingUnreadShowsTheWordsForIt()
    {
        var sparse = Hardware() with
        {
            Cpus = [new CpuInfo(null, "Ryzen 7", null, null, null)],
            Gpus = [new GpuInfo(null, null, null, null, GpuKind.Discrete, "0:1")],
            Motherboard = new MotherboardInfo(null, null, null, null),
            Disks = [new DiskInfo("0", null, null, null, null, null, false)],
            Displays = [new DisplayInfo(null, null, null, null, null, null, null, null, null, false, null)],
        };

        Assert.DoesNotContain(HardwareSummary.Lines(Chinese, sparse), line => line.Value.Contains(Chinese["common.unavailable"]));
    }

    [Fact]
    public void TwoDrivesShareOneLabelAndKeepTheirOwnHealth()
    {
        DiskInfo Disk(string id, string model) => new(id, model, 1_000_000_000_000, "SSD", "NVMe", "Healthy", id == "0");
        DiskHealth Health(string id, int life) => new(id, null, DiskHealthStatus.Good, life, null, null, null, null, null, null, null, null, null, null, null, null, null, []);
        var hardware = Hardware() with { Disks = [Disk("0", "Alpha"), Disk("1", "Beta")], DiskHealth = [Health("1", 80), Health("0", 99)] };

        var lines = HardwareSummary.Lines(Chinese, hardware);
        var rows = lines.Single(line => line.Label == "硬盘").Value.Split(Environment.NewLine);

        Assert.DoesNotContain(lines, line => line.Label == "硬盘健康");
        Assert.Equal(4, rows.Length);
        Assert.Contains("Alpha", rows[0]);
        Assert.StartsWith("良好 99%", rows[1]);
        Assert.Contains("Beta", rows[2]);
        Assert.StartsWith("良好 80%", rows[3]);
    }

    [Theory]
    [InlineData("スピーカー (Realtek(R) Audio)", "Realtek(R) Audio")]
    [InlineData("マイク配列 (Realtek(R) Audio)", "Realtek(R) Audio")]
    [InlineData("Speakers (Logitech G733)", "Logitech G733")]
    [InlineData("扬声器 (Realtek Audio)", "Realtek Audio")]
    [InlineData("LG Ultra HD (Intel Display Audio)", "LG Ultra HD (Intel Display Audio)")]
    [InlineData("ROG Headset", "ROG Headset")]
    public void GenericEndpointKindsGiveWayToTheDeviceName(string name, string expected)
    {
        Assert.Equal(expected, AudioDeviceReader.CleanEndpointName(name));
    }

    [Theory]
    [InlineData(936, "GBK")]
    [InlineData(932, "Shift-JIS")]
    [InlineData(65001, "UTF-8")]
    [InlineData(1252, "Windows-1252")]
    public void SystemEncodingsHaveTheirUsualNames(int codePage, string expected)
    {
        Assert.Equal(expected, DisplayNames.EncodingName(codePage));
    }

    [Fact]
    public void ChineseTitlesSayProcessorAndGraphicsCard()
    {
        foreach (var language in new[] { AppLanguage.SIMPLIFIED_CHINESE, AppLanguage.TRADITIONAL_CHINESE })
        {
            var t = Translator.Load(language);
            foreach (var key in t.Keys.Where(key => key.StartsWith("ui.", StringComparison.Ordinal) || key.StartsWith("phase.", StringComparison.Ordinal) || key.StartsWith("metric.", StringComparison.Ordinal)))
            {
                Assert.DoesNotContain("CPU", t[key]);
                Assert.DoesNotContain("GPU", t[key]);
            }
        }
    }

    private static AudioCandidate Audio(string name, string? bus, uint? formFactor) => new(name, bus, formFactor);

    [Theory]
    [InlineData("HDAUDIO", 1u, true)]
    [InlineData("HDAUDIO", 3u, true)]
    [InlineData("USB", 10u, true)]
    [InlineData("BTHENUM", 5u, true)]
    [InlineData("HDAUDIO", 9u, true)]
    [InlineData("HDAUDIO", 8u, false)]
    [InlineData("ROOT", 1u, false)]
    public void OnlyPluggedInSpeakersCountAsAudioOutput(string bus, uint formFactor, bool expected)
    {
        Assert.Equal(expected, AudioDeviceReader.IsRealOutput(Audio("x", bus, formFactor)));
    }

    [Theory]
    [InlineData("HDAUDIO", 4u, true)]
    [InlineData("USB", 10u, true)]
    [InlineData("HDAUDIO", 10u, false)]
    [InlineData("ROOT", 4u, false)]
    public void StereoMixAndVirtualInputsAreNotMicrophones(string bus, uint formFactor, bool expected)
    {
        Assert.Equal(expected, AudioDeviceReader.IsRealInput(Audio("x", bus, formFactor)));
    }

    [Fact]
    public void MonitorAudioCountsAsTheSpeakerButADigitalPortDoesNot()
    {
        var monitor = Audio("LG Ultra HD", "HDAUDIO", 9);
        var digital = Audio("Digital Output", "HDAUDIO", 8);

        Assert.Equal("LG Ultra HD", AudioDeviceReader.Choose(digital, [digital, monitor], AudioDeviceReader.IsRealOutput)?.Name);
        Assert.Null(AudioDeviceReader.Choose(digital, [digital], AudioDeviceReader.IsRealOutput));
    }

    [Theory]
    [InlineData("HID_DEVICE_UP:000D_U:0005", PeripheralKind.Touchpad)]
    [InlineData("HID_DEVICE_UP:000D_U:0004", PeripheralKind.Touchscreen)]
    [InlineData("HID_DEVICE_UP:0001_U:0005", PeripheralKind.Gamepad)]
    [InlineData("HID_DEVICE_UP:0001_U:0004", PeripheralKind.Gamepad)]
    public void HidUsageIdentifiesDevicesInAnyLanguage(string hardwareId, PeripheralKind expected)
    {
        Assert.Equal(expected, PeripheralCollector.Classify("符合 HID 标准的设备", "HIDClass", @"HID\VID_1", [hardwareId])?.Kind);
    }

    [Fact]
    public void OtherHidDevicesAreIgnored()
    {
        Assert.Null(PeripheralCollector.Classify("Vendor device", "HIDClass", @"HID\VID_1", ["HID_DEVICE_UP:FF00_U:0001"]));
    }

    [Theory]
    [InlineData("Synaptics Pointing Device")]
    [InlineData("ELAN Touchpad")]
    public void VendorTouchpadsAreNotMiceOrKeyboards(string name)
    {
        Assert.Equal(PeripheralKind.Touchpad, PeripheralCollector.Classify(name, "Mouse", @"ACPI\ELAN")?.Kind);
    }

    [Theory]
    [InlineData("MediaTek Wi-Fi 7 MT7925 Wireless LAN Card", true)]
    [InlineData("Intel Wi-Fi 6 AX201 160MHz", true)]
    [InlineData("Realtek RTL8852BE WiFi 6 802.11ax PCIe Adapter", true)]
    [InlineData("Intel Ethernet Controller I226-V", false)]
    [InlineData("Realtek PCIe GbE Family Controller", false)]
    public void WiredAndWirelessCardsAreToldApart(string name, bool expected)
    {
        Assert.Equal(expected, NetworkCollector.IsWirelessCard(name));
    }

    [Fact]
    public void WiredAndWirelessCardsHaveTheirOwnRowsAndMissingOnesReadAsNone()
    {
        Assert.Equal("Intel Ethernet Controller I226-V", Value(Hardware(), "有线网卡"));
        Assert.Equal("MediaTek Wi-Fi 7 MT7925", Value(Hardware(), "无线网卡"));
        Assert.DoesNotContain(HardwareSummary.Lines(Chinese, Hardware() with { WiredAdapters = [] }), line => line.Label == "有线网卡");
    }

    [Fact]
    public void OptionalDevicesOnlyShowWhenPresent()
    {
        Assert.Empty(HardwareSummary.PeripheralLines(Chinese, []));
    }

    [Theory]
    [InlineData("Intel(R) Ethernet Controller I226-V", @"PCI\VEN_8086", true)]
    [InlineData("Microsoft Wi-Fi Direct Virtual Adapter", @"SWD\WIFI", false)]
    [InlineData("Hyper-V Virtual Ethernet Adapter", @"ROOT\VMS", false)]
    [InlineData("Bluetooth Device (PAN)", @"USB\VID_1", false)]
    public void NetworkCardsAreRealHardwareOnly(string name, string id, bool expected)
    {
        Assert.Equal(expected, PeripheralCollector.IsNetworkCard(name, id));
    }

    [Theory]
    [InlineData("ASUS", "System Product Name", "System Version", 3, false)]
    [InlineData("To Be Filled By O.E.M.", "To Be Filled By O.E.M.", null, 3, false)]
    [InlineData("Dell Inc.", "OptiPlex 7010", null, 3, true)]
    [InlineData("HP", "HP EliteBook 840 G9", null, 10, true)]
    public void SelfBuiltPcsGetNoModelLine(string manufacturer, string model, string? version, int chassis, bool expected)
    {
        Assert.Equal(expected, ComputerCollector.Describe(manufacturer, model, version, [chassis]) is not null);
    }

    [Fact]
    public void LenovoNamesComeFromTheVersionField()
    {
        var computer = ComputerCollector.Describe("LENOVO", "20XW005JUS", "ThinkPad X1 Carbon Gen 9", [10]);

        Assert.Equal("ThinkPad X1 Carbon Gen 9", computer?.Model);
        Assert.True(computer?.IsLaptop);
    }

    [Fact]
    public void LaptopWithoutFirmwareNamesIsStillALaptop()
    {
        var computer = ComputerCollector.Describe("To Be Filled By O.E.M.", "Default string", null, [9]);

        Assert.True(computer?.IsLaptop);
        Assert.Null(computer?.Model);
    }

    [Fact]
    public void LaptopLineAndBatteryAppearInTheSummary()
    {
        var hardware = Hardware() with
        {
            Computer = new ComputerInfo("HP", "EliteBook 840 G9", true),
            Battery = new BatteryInfo(52000, 45240, ComputerCollector.HealthPercent(52000, 45240)),
        };

        Assert.Equal("HP EliteBook 840 G9", Value(hardware, "笔记本电脑"));
        Assert.Equal("健康度 87%（当前 45.2Wh / 设计 52Wh）", Value(hardware, "电池"));
        Assert.DoesNotContain(HardwareSummary.Lines(Chinese, hardware, isFull: false), line => line.Label == "电池");
    }

    [Theory]
    [InlineData(50000UL, 25000UL, 50)]
    [InlineData(50000UL, 60000UL, 100)]
    [InlineData(0UL, 100UL, 0)]
    public void BatteryHealthIsCappedAndSafe(ulong designed, ulong full, int expected)
    {
        Assert.Equal(expected, ComputerCollector.HealthPercent(designed, full));
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-14700K CPU @ 3.40GHz", "Intel Core i7-14700K")]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", "AMD Ryzen 7 7800X3D")]
    [InlineData("NVIDIA GeForce RTX 4070", "NVIDIA GeForce RTX 4070")]
    public void ModelNamesLoseMarksAndSuffixes(string raw, string expected)
    {
        Assert.Equal(expected, DisplayNames.CleanModel(raw));
    }

    [Theory]
    [InlineData("CT2000T700SSD3", "Crucial")]
    [InlineData("WDC WD10EZEX", "Western Digital")]
    [InlineData("Samsung SSD 990 PRO", null)]
    [InlineData("WD_BLACK SN850X", null)]
    [InlineData("Unknown Drive", null)]
    public void DiskBrandComesFromKnownPrefixesOnly(string model, string? expected)
    {
        Assert.Equal(expected, DisplayNames.DiskBrand(model));
    }

    [Theory]
    [InlineData(500_107_862_016UL, "500GB")]
    [InlineData(1_000_204_886_016UL, "1TB")]
    [InlineData(2_000_398_934_016UL, "2TB")]
    public void DiskSizesUseRetailUnits(ulong bytes, string expected)
    {
        Assert.Equal(expected, DisplayNames.DiskSize(bytes));
    }

    private static NetworkCandidate Nic(string name, NetworkInterfaceType type, bool isUp = true, bool hasGateway = true, long speed = 1_000_000_000) =>
        new(name, type, isUp, hasGateway, speed);

    [Fact]
    public void WiFiIsReportedWithItsLinkSpeed()
    {
        var result = NetworkCollector.Choose([Nic("MediaTek Wi-Fi 7", NetworkInterfaceType.Wireless80211, speed: 2_882_000_000), Nic("Intel I226-V", NetworkInterfaceType.Ethernet, isUp: false)]);

        Assert.Equal(NetworkKind.WiFi, result.Kind);
        Assert.Equal(2882, result.LinkMegabits);
    }

    [Fact]
    public void WiredWinsWhenBothAreConnected()
    {
        var result = NetworkCollector.Choose([Nic("Wi-Fi", NetworkInterfaceType.Wireless80211), Nic("Intel I226-V", NetworkInterfaceType.Ethernet)]);

        Assert.Equal(NetworkKind.Wired, result.Kind);
    }

    [Fact]
    public void VirtualAdaptersAreNotANetworkConnection()
    {
        var result = NetworkCollector.Choose([Nic("Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet), Nic("Bluetooth Device (Personal Area Network)", NetworkInterfaceType.Ethernet), Nic("TAP-Windows Adapter", NetworkInterfaceType.Ethernet)]);

        Assert.Equal(NetworkKind.None, result.Kind);
    }

    [Fact]
    public void AdapterWithoutGatewayIsNotConnected()
    {
        var result = NetworkCollector.Choose([Nic("Intel I226-V", NetworkInterfaceType.Ethernet, hasGateway: false)]);

        Assert.Equal(NetworkKind.None, result.Kind);
    }

    [Fact]
    public void ExtraAdaptersOfOnePhysicalGpuAreDropped()
    {
        string[] adapterIds = ["VEN_8086&DEV_E211", "VEN_8086&DEV_7D67", "VEN_8086&DEV_E211"];

        var kept = GpuInfoCollector.KeepPhysicalAdapters(adapterIds, id => id == "VEN_8086&DEV_E211" ? 1 : 1);

        Assert.Equal([0, 1], kept);
    }

    [Fact]
    public void TwoRealIdenticalGpusAreBothKept()
    {
        string[] adapterIds = ["VEN_10DE&DEV_2786", "VEN_10DE&DEV_2786"];

        Assert.Equal([0, 1], GpuInfoCollector.KeepPhysicalAdapters(adapterIds, _ => 2));
    }

    [Fact]
    public void GpuMissingFromTheDeviceListIsNotDropped()
    {
        string[] adapterIds = ["VEN_1414&DEV_008C", "VEN_1414&DEV_008C"];

        Assert.Equal([0, 1], GpuInfoCollector.KeepPhysicalAdapters(adapterIds, _ => 0));
    }
}
