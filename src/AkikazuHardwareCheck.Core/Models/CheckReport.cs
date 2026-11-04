namespace AkikazuHardwareCheck.Core.Models;

/// <summary>The one data model behind the JSON technical record and the customer report.</summary>
public sealed record CheckReport(
    int SchemaVersion,
    string ToolVersion,
    DateTimeOffset GeneratedAt,
    HardwareInventory Hardware,
    IReadOnlyList<SensorReading> Sensors,
    CoolingEstimate Cooling,
    TestRunResult? TestRun = null,
    MemoryTestResult? MemoryTest = null,
    DiskTestResult? DiskTest = null,
    AudioCheckResult? AudioCheck = null,
    IReadOnlyList<TemperatureRow>? Temperatures = null,
    IReadOnlyList<LoadMetricRow>? LoadMetrics = null)
{
    public const int CURRENT_SCHEMA_VERSION = 7;

    public static string CurrentToolVersion { get; } = typeof(CheckReport).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}

public sealed record HardwareInventory(
    ProbeEnvironment Environment,
    IReadOnlyList<CpuInfo> Cpus,
    IReadOnlyList<GpuInfo> Gpus,
    MotherboardInfo? Motherboard,
    MemoryInfo? Memory,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<DisplayInfo> Displays,
    IReadOnlyList<DiagnosticEntry> Diagnostics,
    NetworkConnection? Network = null,
    SystemDriveSpace? SystemDrive = null,
    IReadOnlyList<DiskHealth>? DiskHealth = null,
    IReadOnlyList<Peripheral>? Peripherals = null,
    IReadOnlyList<string>? WiredAdapters = null,
    IReadOnlyList<string>? WirelessAdapters = null,
    ComputerInfo? Computer = null,
    BatteryInfo? Battery = null,
    SecurityInfo? Security = null,
    LocaleInfo? Locale = null,
    ReliabilityInfo? Reliability = null);

/// <summary>Windows hardware error reports over the recent window, the CPU microcode revision and the last boot time. Null means unread.</summary>
public sealed record ReliabilityInfo(int? WheaErrors, string? MicrocodeRevision, DateTimeOffset? LastBootAt);

/// <summary>Language codes as Windows reports them: display language, regional format and system locale, plus the code page older programs use.</summary>
public sealed record LocaleInfo(string DisplayLanguage, string Format, string SystemLocale, int AnsiCodePage);

/// <summary>Null means the state could not be read, or Secure Boot does not exist on this boot mode.</summary>
public sealed record SecurityInfo(bool? SecureBootEnabled, string? TpmVersion);

/// <summary>What staff heard. Null means that test was not run.</summary>
public sealed record AudioCheckResult(bool? SpeakerHeard, bool? MicrophoneHeard);

/// <summary>A branded PC or laptop. Manufacturer and Model are null for a laptop whose firmware holds only placeholders.</summary>
public sealed record ComputerInfo(string? Manufacturer, string? Model, bool IsLaptop);

public sealed record BatteryInfo(ulong DesignedMilliwattHours, ulong FullMilliwattHours, int HealthPercent);

public enum NetworkKind
{
    WiFi,
    Wired,
    None,
}

/// <summary>How the PC reaches the network now. Addresses and network names are deliberately absent.</summary>
public sealed record NetworkConnection(NetworkKind Kind, string? Adapter, long? LinkMegabits, int? SignalPercent = null);

public sealed record SystemDriveSpace(ulong UsedBytes, ulong TotalBytes);

public sealed record ProbeEnvironment(
    OperatingSystemInfo? OperatingSystem,
    WindowsActivation Activation,
    bool IsAdministrator,
    bool IsPawnIoInstalled)
{
    /// <summary>Super I/O, embedded controller and CPU MSR sensors need both administrator rights and PawnIO.</summary>
    public bool IsSensorAccessLimited => !IsAdministrator || !IsPawnIoInstalled;
}

public sealed record OperatingSystemInfo(string? Name, string? DisplayVersion, string? Build, string Architecture);

public enum WindowsActivation
{
    Activated,
    GracePeriod,
    Notification,
    NotActivated,
    Unknown,
}

public sealed record CpuInfo(string? Manufacturer, string? Model, int? PhysicalCores, int? LogicalThreads, int? MaxClockMhz);

public enum GpuKind
{
    Discrete,
    Integrated,
    Software,
    Unknown,
}

public sealed record GpuInfo(
    string? Vendor,
    string? Model,
    ulong? DedicatedMemoryBytes,
    string? DriverVersion,
    GpuKind Kind,
    string? AdapterLuid)
{
    public bool IsSoftwareAdapter => Kind == GpuKind.Software;
}

public sealed record MotherboardInfo(string? Manufacturer, string? Model, string? BiosVersion, string? BiosDate);

public sealed record MemoryInfo(ulong? SystemVisibleBytes, ulong? InstalledBytes, IReadOnlyList<MemoryModule> Modules);

public sealed record MemoryModule(
    string? Slot,
    ulong? CapacityBytes,
    string? MemoryType,
    string? Manufacturer,
    string? PartNumber,
    int? RatedSpeedMts,
    int? ConfiguredSpeedMts,
    int? ConfiguredMillivolts = null);

public sealed record DiskInfo(
    string? DeviceId,
    string? Model,
    ulong? SizeBytes,
    string? MediaType,
    string? BusType,
    string? HealthStatus,
    bool IsSystemDisk,
    int? PcieGeneration = null,
    int? PcieLanes = null);

public sealed record DisplayInfo(
    string? Name,
    string? ManufacturerCode,
    string? ProductCode,
    int? YearOfManufacture,
    int? WidthPixels,
    int? HeightPixels,
    double? RefreshRateHz,
    double? DiagonalInches,
    string? Connection,
    bool IsPrimary,
    string? ConnectedGpu);

public enum SensorStatus
{
    Ok,
    NoValue,
    Invalid,
}

public sealed record SensorReading(
    string HardwareId,
    string HardwareName,
    string HardwareType,
    string SensorId,
    string SensorName,
    string SensorType,
    double? Value,
    string Unit,
    SensorStatus Status,
    string Source,
    DateTimeOffset SampledAt);

public enum CoolingOutcome
{
    LikelyLiquid,
    LikelyAir,
    AbnormalWiring,
    NoData,
}

public enum CoolingNote
{
    PumpHeaderIsNotProof,
    PumpMayBeUnreported,
    CheckCoolerWiring,
    PassiveCoolingPossible,
    UnlabeledFansPresent,
    SensorAccessLimited,
}

public sealed record CoolingEstimate(
    CoolingOutcome Outcome,
    double? PumpRpm,
    double? CpuFanRpm,
    int UnlabeledRunningFans,
    IReadOnlyList<CoolingNote> Notes,
    string? CoolerName = null);

public sealed record DiagnosticEntry(string Source, string Message);
