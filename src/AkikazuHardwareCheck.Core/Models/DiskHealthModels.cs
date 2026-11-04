namespace AkikazuHardwareCheck.Core.Models;

public enum DiskHealthStatus
{
    Good,
    Caution,
    Bad,
    Unknown,
}

public enum DiskFinding
{
    SpareBelowThreshold,
    OverTemperature,
    ReliabilityDegraded,
    ReadOnlyMode,
    BackupFailed,
    MediaErrors,
    LowLife,
    ReallocatedSectors,
    PendingSectors,
    UncorrectableSectors,
    ToolReportedBad,
    NeedsAdministrator,
    NotReadable,
}

/// <param name="DeviceId">Windows disk number.</param>
/// <param name="Model">Drive model.</param>
/// <param name="Status">Overall judgement.</param>
/// <param name="LifePercent">Remaining life. For NVMe this is 100 minus the drive's percentage used.</param>
/// <param name="TemperatureCelsius">Composite temperature.</param>
/// <param name="PowerOnHours">Hours powered on.</param>
/// <param name="PowerCycles">Power cycle count.</param>
/// <param name="UnsafeShutdowns">NVMe unsafe shutdowns.</param>
/// <param name="MediaErrors">NVMe media and data integrity errors: ID 0E in CrystalDiskInfo.</param>
/// <param name="ErrorLogEntries">NVMe error log entries, which often include harmless commands.</param>
/// <param name="AvailableSparePercent">NVMe available spare.</param>
/// <param name="SpareThresholdPercent">NVMe available spare threshold.</param>
/// <param name="DataReadBytes">Total host reads.</param>
/// <param name="DataWrittenBytes">Total host writes.</param>
/// <param name="ReallocatedSectors">ATA attribute 05 raw value.</param>
/// <param name="PendingSectors">ATA attribute C5 raw value.</param>
/// <param name="UncorrectableSectors">ATA attribute C6 raw value.</param>
/// <param name="Findings">The reasons behind the judgement.</param>
public sealed record DiskHealth(
    string? DeviceId,
    string? Model,
    DiskHealthStatus Status,
    int? LifePercent = null,
    int? TemperatureCelsius = null,
    ulong? PowerOnHours = null,
    ulong? PowerCycles = null,
    ulong? UnsafeShutdowns = null,
    ulong? MediaErrors = null,
    ulong? ErrorLogEntries = null,
    int? AvailableSparePercent = null,
    int? SpareThresholdPercent = null,
    ulong? DataReadBytes = null,
    ulong? DataWrittenBytes = null,
    ulong? ReallocatedSectors = null,
    ulong? PendingSectors = null,
    ulong? UncorrectableSectors = null,
    IReadOnlyList<DiskFinding>? Findings = null);

public enum PeripheralKind
{
    Input,
    Speaker,
    Microphone,
    Camera,
    Printer,
    Touchpad,
    Touchscreen,
    Gamepad,
}

/// <summary>A connected device. Name is null when the device reports only a generic name such as "HID Keyboard Device".</summary>
public sealed record Peripheral(PeripheralKind Kind, string? Name, bool IsBluetooth);
