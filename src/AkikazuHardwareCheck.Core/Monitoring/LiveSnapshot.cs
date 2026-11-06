namespace AkikazuHardwareCheck.Core.Monitoring;

/// <summary>One UI-side live sample shared by the monitor and the graph window. No sensor source is opened here.</summary>
public sealed record LiveSnapshot(
    DateTimeOffset At,
    double? CpuLoad,
    double? CpuTemperature,
    double? GpuLoad,
    double? GpuTemperature,
    double? MemoryLoad,
    double? DiskBusy,
    double? UploadBytesPerSecond,
    double? DownloadBytesPerSecond,
    string? NetworkAdapter);
