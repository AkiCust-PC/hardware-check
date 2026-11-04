namespace AkikazuHardwareCheck.Core.Models;

public enum MemoryTestEnd
{
    Completed,
    ErrorsFound,
    Stopped,
    Unsupported,
    Failed,
}

public sealed record MemoryTestResult(MemoryTestEnd End, ulong TestedBytes, long Passes, long Errors, double Seconds, string? Detail);

public enum DiskTestEnd
{
    Completed,
    ErrorsFound,
    Stopped,
    NotEnoughSpace,
    Failed,
}

/// <param name="End">How the test ended.</param>
/// <param name="Drive">The tested drive's root.</param>
/// <param name="SizeBytes">Size of the temporary file.</param>
/// <param name="SequentialWriteMbps">Sequential write speed in MB/s.</param>
/// <param name="SequentialReadMbps">Sequential read speed in MB/s.</param>
/// <param name="RandomReadMbps">4 KiB random read speed in MB/s.</param>
/// <param name="VerificationErrors">Blocks that did not read back as written.</param>
/// <param name="Detail">Technical detail for the record.</param>
public sealed record DiskTestResult(
    DiskTestEnd End,
    string Drive,
    long SizeBytes,
    double? SequentialWriteMbps,
    double? SequentialReadMbps,
    double? RandomReadMbps,
    long VerificationErrors,
    string? Detail);
