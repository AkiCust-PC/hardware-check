using System.Diagnostics;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace AkikazuHardwareCheck.Core.Testing;

/// <summary>
/// A short, non-destructive drive check in the style of CrystalDiskMark: it writes one new temporary file of 1 GiB,
/// reads it back with verification, measures random reads, then deletes the file. Existing data is never touched.
/// Speeds are single-queue figures, so they run lower than the Q8 numbers on drive boxes.
/// </summary>
public sealed class DiskTest(string folder, long sizeBytes = DiskTest.DEFAULT_SIZE_BYTES)
{
    public const long DEFAULT_SIZE_BYTES = 1024L * 1024 * 1024;

    // Windows and other programs need room to keep working after the test file is written.
    private const long MIN_SPARE_BYTES = 5L * 1024 * 1024 * 1024;

    private const int BLOCK_BYTES = 1024 * 1024;
    private const int RANDOM_BLOCK_BYTES = 4096;

    // Unbuffered I/O needs sector-aligned memory. 4096 bytes covers every common sector size.
    private const int ALIGNMENT_BYTES = 4096;

    // FILE_FLAG_NO_BUFFERING: bypasses the Windows cache, so speeds and verification reach the drive itself.
    private const FileOptions NO_BUFFERING = (FileOptions)0x20000000;

    private const double BYTES_PER_MB = 1000 * 1000;
    private const int STAMP_BYTES = sizeof(long);
    private const int RANDOM_SEED = 20261104;

    private static readonly TimeSpan RandomReadTime = TimeSpan.FromSeconds(5);

    public Task<DiskTestResult> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Run(progress, cancellationToken), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>Refuses to run when the file would leave the drive too full.</summary>
    public static bool HasRoomFor(long freeBytes, long sizeBytes) => freeBytes >= sizeBytes + MIN_SPARE_BYTES;

    /// <summary>Picks a writable location: the temp folder on the system's own drive, otherwise the drive's root.</summary>
    public static string FolderForDrive(string driveRoot, string tempFolder) =>
        string.Equals(Path.GetPathRoot(tempFolder), driveRoot, StringComparison.OrdinalIgnoreCase) ? tempFolder : driveRoot;

    private DiskTestResult Run(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, $"AkikazuHardwareCheck-disktest-{Guid.NewGuid():N}.tmp");
        var root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? folder;
        try
        {
            if (!HasRoomFor(new DriveInfo(root).AvailableFreeSpace, sizeBytes))
            {
                return new DiskTestResult(DiskTestEnd.NotEnoughSpace, root, sizeBytes, null, null, null, 0, null);
            }

            var pattern = new byte[BLOCK_BYTES];
            new Random(RANDOM_SEED).NextBytes(pattern);
            var holder = GC.AllocateUninitializedArray<byte>(BLOCK_BYTES + ALIGNMENT_BYTES, pinned: true);
            var offset = AlignmentOffset(holder);
            var block = holder.AsMemory(offset, BLOCK_BYTES);

            using var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, NO_BUFFERING | FileOptions.WriteThrough | FileOptions.DeleteOnClose, sizeBytes);
            var write = Sequential(handle, pattern, block, isWrite: true, progress, cancellationToken);
            var read = write is null ? null : Sequential(handle, pattern, block, isWrite: false, progress, cancellationToken);
            if (write is null || read is null)
            {
                return new DiskTestResult(DiskTestEnd.Stopped, root, sizeBytes, write?.Speed, read?.Speed, null, read?.Errors ?? 0, null);
            }

            if (read.Errors > 0)
            {
                return new DiskTestResult(DiskTestEnd.ErrorsFound, root, sizeBytes, write.Speed, read.Speed, null, read.Errors, null);
            }

            var random = RandomRead(handle, block, cancellationToken);
            return new DiskTestResult(cancellationToken.IsCancellationRequested ? DiskTestEnd.Stopped : DiskTestEnd.Completed, root, sizeBytes, write.Speed, read.Speed, random, 0, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new DiskTestResult(DiskTestEnd.Failed, root, sizeBytes, null, null, null, 0, exception.Message);
        }
    }

    private static int AlignmentOffset(byte[] pinned)
    {
        var address = Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0).ToInt64();
        return (int)((ALIGNMENT_BYTES - (address % ALIGNMENT_BYTES)) % ALIGNMENT_BYTES);
    }

    private PassResult? Sequential(SafeFileHandle handle, byte[] pattern, Memory<byte> block, bool isWrite, IProgress<double>? progress, CancellationToken stop)
    {
        var blocks = sizeBytes / BLOCK_BYTES;
        var errors = 0L;
        var clock = Stopwatch.StartNew();
        var span = block.Span;
        if (isWrite)
        {
            pattern.CopyTo(span);
        }

        for (long index = 0; index < blocks; index++)
        {
            if (stop.IsCancellationRequested)
            {
                return null;
            }

            var position = index * BLOCK_BYTES;
            if (isWrite)
            {
                // A stamp per block makes every block unique, so a drive returning the wrong block is caught.
                BitConverter.TryWriteBytes(span, index);
                RandomAccess.Write(handle, span, position);
            }
            else
            {
                errors += ReadAndVerify(handle, span, position, index, pattern);
            }

            progress?.Report((isWrite ? 0 : 0.5) + ((double)index / blocks / 2));
        }

        return new PassResult(blocks * BLOCK_BYTES / clock.Elapsed.TotalSeconds / BYTES_PER_MB, errors);
    }

    private static long ReadAndVerify(SafeFileHandle handle, Span<byte> span, long position, long index, byte[] pattern)
    {
        var read = RandomAccess.Read(handle, span, position);
        return read == span.Length && IsBlockValid(span, index, pattern) ? 0 : 1;
    }

    /// <summary>A block is valid when its stamp names this block and the rest matches the pattern.</summary>
    public static bool IsBlockValid(ReadOnlySpan<byte> block, long index, ReadOnlySpan<byte> pattern) =>
        BitConverter.ToInt64(block) == index && block[STAMP_BYTES..].SequenceEqual(pattern[STAMP_BYTES..]);

    private double? RandomRead(SafeFileHandle handle, Memory<byte> block, CancellationToken stop)
    {
        var slots = sizeBytes / RANDOM_BLOCK_BYTES;
        var random = new Random(RANDOM_SEED);
        var span = block.Span[..RANDOM_BLOCK_BYTES];
        var clock = Stopwatch.StartNew();
        var total = 0L;
        while (clock.Elapsed < RandomReadTime && !stop.IsCancellationRequested)
        {
            total += RandomAccess.Read(handle, span, random.NextInt64(slots) * RANDOM_BLOCK_BYTES);
        }

        return clock.Elapsed.TotalSeconds > 0 ? total / clock.Elapsed.TotalSeconds / BYTES_PER_MB : null;
    }

    private sealed record PassResult(double Speed, long Errors);
}
