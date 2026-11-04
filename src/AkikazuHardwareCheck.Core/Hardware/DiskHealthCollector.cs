using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;
using DiskInfoToolkit;
using Microsoft.Win32.SafeHandles;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// Reads drive health without writing anything. NVMe drives answer the standard health log directly, usually without
/// administrator rights. Everything else goes through DiskInfoToolkit, which needs those rights. Judgement rules follow
/// CrystalDiskInfo's published ATA rules and the NVMe specification's critical warning bits.
/// </summary>
public static class DiskHealthCollector
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    private const uint FILE_SHARE_READ_WRITE = 3;
    private const uint OPEN_EXISTING = 3;

    // STORAGE_PROPERTY_ID values: device first, then adapter, as drivers differ in which they answer.
    private static readonly uint[] ProtocolProperties = [50, 49];

    // STORAGE_PROTOCOL_SPECIFIC_DATA follows the 8-byte query header; the log follows it at this offset.
    private const uint PROTOCOL_TYPE_NVME = 3;
    private const uint NVME_DATA_TYPE_LOG_PAGE = 2;
    private const uint HEALTH_LOG_PAGE = 2;
    private const int QUERY_HEADER_BYTES = 8;
    private const uint PROTOCOL_DATA_OFFSET = 40;
    private const int LOG_START = QUERY_HEADER_BYTES + (int)PROTOCOL_DATA_OFFSET;
    private const int LOG_BYTES = 512;

    // NVMe health log layout (NVM Express base specification, log page 02h).
    private const int CRITICAL_WARNING_OFFSET = 0;
    private const int TEMPERATURE_OFFSET = 1;
    private const int SPARE_OFFSET = 3;
    private const int SPARE_THRESHOLD_OFFSET = 4;
    private const int PERCENTAGE_USED_OFFSET = 5;
    private const int UNITS_READ_OFFSET = 32;
    private const int UNITS_WRITTEN_OFFSET = 48;
    private const int POWER_CYCLES_OFFSET = 112;
    private const int POWER_ON_HOURS_OFFSET = 128;
    private const int UNSAFE_SHUTDOWNS_OFFSET = 144;
    private const int MEDIA_ERRORS_OFFSET = 160;
    private const int ERROR_LOG_ENTRIES_OFFSET = 176;
    private const int KELVIN_OFFSET = 273;

    // One NVMe data unit is 1000 units of 512 bytes.
    private const ulong BYTES_PER_DATA_UNIT = 512_000;

    private const int FULL_LIFE_PERCENT = 100;

    // CrystalDiskInfo cautions at 10% remaining life.
    private const int LOW_LIFE_PERCENT = 10;

    // Critical warning bits from the NVMe specification.
    private const byte WARNING_SPARE = 1 << 0;
    private const byte WARNING_TEMPERATURE = 1 << 1;
    private const byte WARNING_RELIABILITY = 1 << 2;
    private const byte WARNING_READ_ONLY = 1 << 3;
    private const byte WARNING_BACKUP = 1 << 4;
    private const byte WARNING_PMR_READ_ONLY = 1 << 5;

    private const byte ATTRIBUTE_REALLOCATED = 0x05;
    private const byte ATTRIBUTE_PENDING = 0xC5;
    private const byte ATTRIBUTE_UNCORRECTABLE = 0xC6;

    public static List<DiskHealth> Read(IReadOnlyList<DiskInfo> disks, bool isAdministrator)
    {
        var toolkit = new Lazy<List<Storage>>(() => ReadToolkit(isAdministrator));
        return disks.Select(disk => ReadDisk(disk, toolkit, isAdministrator)).ToList();
    }

    /// <summary>Parses a raw NVMe health log. Counters are 128-bit, but the low 64 bits cover any real drive's lifetime.</summary>
    public static DiskHealth ParseNvmeLog(string? deviceId, string? model, ReadOnlySpan<byte> log)
    {
        var warning = log[CRITICAL_WARNING_OFFSET];
        var spare = log[SPARE_OFFSET];
        var threshold = log[SPARE_THRESHOLD_OFFSET];
        var life = Math.Max(0, FULL_LIFE_PERCENT - log[PERCENTAGE_USED_OFFSET]);
        var mediaErrors = BitConverter.ToUInt64(log[MEDIA_ERRORS_OFFSET..]);
        var kelvin = BitConverter.ToUInt16(log[TEMPERATURE_OFFSET..]);
        var (status, findings) = JudgeNvme(warning, mediaErrors, life);

        return new DiskHealth(
            deviceId,
            model,
            status,
            life,
            kelvin > 0 ? kelvin - KELVIN_OFFSET : null,
            BitConverter.ToUInt64(log[POWER_ON_HOURS_OFFSET..]),
            BitConverter.ToUInt64(log[POWER_CYCLES_OFFSET..]),
            BitConverter.ToUInt64(log[UNSAFE_SHUTDOWNS_OFFSET..]),
            mediaErrors,
            BitConverter.ToUInt64(log[ERROR_LOG_ENTRIES_OFFSET..]),
            spare,
            threshold,
            BitConverter.ToUInt64(log[UNITS_READ_OFFSET..]) * BYTES_PER_DATA_UNIT,
            BitConverter.ToUInt64(log[UNITS_WRITTEN_OFFSET..]) * BYTES_PER_DATA_UNIT,
            Findings: findings);
    }

    /// <summary>Spare exhaustion, degraded reliability and read-only mode mean the drive itself reports failure.</summary>
    public static (DiskHealthStatus Status, List<DiskFinding> Findings) JudgeNvme(byte warning, ulong mediaErrors, int lifePercent)
    {
        var findings = new List<DiskFinding>();
        void AddIf(bool condition, DiskFinding finding)
        {
            if (condition)
            {
                findings.Add(finding);
            }
        }

        AddIf((warning & WARNING_SPARE) != 0, DiskFinding.SpareBelowThreshold);
        AddIf((warning & WARNING_RELIABILITY) != 0, DiskFinding.ReliabilityDegraded);
        AddIf((warning & (WARNING_READ_ONLY | WARNING_PMR_READ_ONLY)) != 0, DiskFinding.ReadOnlyMode);
        var isBad = findings.Count > 0;

        AddIf((warning & WARNING_TEMPERATURE) != 0, DiskFinding.OverTemperature);
        AddIf((warning & WARNING_BACKUP) != 0, DiskFinding.BackupFailed);
        AddIf(mediaErrors > 0, DiskFinding.MediaErrors);
        AddIf(lifePercent <= LOW_LIFE_PERCENT, DiskFinding.LowLife);

        var status = isBad ? DiskHealthStatus.Bad : findings.Count > 0 ? DiskHealthStatus.Caution : DiskHealthStatus.Good;
        return (status, findings);
    }

    /// <summary>CrystalDiskInfo's default: any raw value of 1 or more on 05, C5 or C6 is a caution.</summary>
    public static (DiskHealthStatus Status, List<DiskFinding> Findings) JudgeAta(ulong? reallocated, ulong? pending, ulong? uncorrectable, int? lifePercent, bool toolSaysBad)
    {
        var findings = new List<DiskFinding>();
        if (toolSaysBad)
        {
            findings.Add(DiskFinding.ToolReportedBad);
        }

        if (reallocated > 0)
        {
            findings.Add(DiskFinding.ReallocatedSectors);
        }

        if (pending > 0)
        {
            findings.Add(DiskFinding.PendingSectors);
        }

        if (uncorrectable > 0)
        {
            findings.Add(DiskFinding.UncorrectableSectors);
        }

        if (lifePercent <= LOW_LIFE_PERCENT)
        {
            findings.Add(DiskFinding.LowLife);
        }

        var status = toolSaysBad ? DiskHealthStatus.Bad : findings.Count > 0 ? DiskHealthStatus.Caution : DiskHealthStatus.Good;
        return (status, findings);
    }

    private static DiskHealth ReadDisk(DiskInfo disk, Lazy<List<Storage>> toolkit, bool isAdministrator)
    {
        if (disk.BusType == "NVMe" && ReadNvme(disk) is { } nvme)
        {
            return nvme;
        }

        var storage = toolkit.Value.FirstOrDefault(item => item.DriveNumber.ToString() == disk.DeviceId);
        if (storage is not null)
        {
            return FromToolkit(disk, storage);
        }

        return new DiskHealth(disk.DeviceId, disk.Model, DiskHealthStatus.Unknown, Findings: [isAdministrator ? DiskFinding.NotReadable : DiskFinding.NeedsAdministrator]);
    }

    private static DiskHealth? ReadNvme(DiskInfo disk)
    {
        using var handle = CreateFileW($@"\\.\PhysicalDrive{disk.DeviceId}", 0, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new byte[LOG_START + LOG_BYTES];
        foreach (var property in ProtocolProperties)
        {
            WriteQuery(buffer, property);
            if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out _, IntPtr.Zero))
            {
                return ParseNvmeLog(disk.DeviceId, disk.Model, buffer.AsSpan(LOG_START, LOG_BYTES));
            }
        }

        return null;
    }

    private static void WriteQuery(byte[] buffer, uint property)
    {
        Array.Clear(buffer);
        uint[] header = [property, 0, PROTOCOL_TYPE_NVME, NVME_DATA_TYPE_LOG_PAGE, HEALTH_LOG_PAGE, 0, PROTOCOL_DATA_OFFSET, LOG_BYTES];
        Buffer.BlockCopy(header, 0, buffer, 0, header.Length * sizeof(uint));
    }

    private static List<Storage> ReadToolkit(bool isAdministrator)
    {
        if (!isAdministrator)
        {
            return [];
        }

        try
        {
            StorageManager.ReloadStorages();
            return [.. StorageManager.Storages];
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ExternalException)
        {
            return [];
        }
    }

    private static DiskHealth FromToolkit(DiskInfo disk, Storage storage)
    {
        var smart = storage.Smart;
        var raw = (smart.SmartAttributes ?? []).ToDictionary(attribute => attribute.Info.ID, attribute => attribute.Attribute.RawValueULong);
        ulong? Raw(byte id) => raw.TryGetValue(id, out var value) ? value : null;
        var reallocated = Raw(ATTRIBUTE_REALLOCATED);
        var pending = Raw(ATTRIBUTE_PENDING);
        var uncorrectable = Raw(ATTRIBUTE_UNCORRECTABLE);
        var life = (int?)smart.Life;
        var (status, findings) = JudgeAta(reallocated, pending, uncorrectable, life, smart.DiskStatus == DiskInfoToolkit.Enums.Interop.DiskStatus.Bad);

        return new DiskHealth(
            disk.DeviceId,
            disk.Model,
            status,
            life,
            (int?)smart.Temperature,
            smart.MeasuredPowerOnHours,
            smart.PowerOnCount,
            ReallocatedSectors: reallocated,
            PendingSectors: pending,
            UncorrectableSectors: uncorrectable,
            Findings: findings);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
}
