using System.Diagnostics;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Testing;

/// <summary>
/// Writes and verifies part of the free memory with patterns from the MemTest86 family: own address, moving inversions
/// and a reproducible random sequence. It runs inside Windows, so it cannot reach memory the system is using and does not replace MemTest86.
/// </summary>
public sealed class MemoryTest(TimeSpan duration, double availableFraction = MemoryTest.DEFAULT_AVAILABLE_FRACTION)
{
    public const double DEFAULT_AVAILABLE_FRACTION = 0.7;

    // Chunks keep each check short, so stopping stays responsive.
    private const int CHUNK_WORDS = 32 * 1024 * 1024;
    private const ulong CHUNK_BYTES = CHUNK_WORDS * sizeof(ulong);
    private const int PATTERN_COUNT = 3;
    private const ulong ALL_ONES = ulong.MaxValue;
    private const ulong RANDOM_SEED = 0x9E3779B97F4A7C15;

    public static TimeSpan DefaultDuration { get; } = TimeSpan.FromMinutes(2);

    public Task<MemoryTestResult> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Run(progress, cancellationToken), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>Fills a chunk so each word holds its own location.</summary>
    public static void FillOwnAddress(Span<ulong> words, ulong baseAddress)
    {
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = baseAddress + (ulong)index;
        }
    }

    public static long CountOwnAddressErrors(ReadOnlySpan<ulong> words, ulong baseAddress)
    {
        var errors = 0L;
        for (var index = 0; index < words.Length; index++)
        {
            if (words[index] != baseAddress + (ulong)index)
            {
                errors++;
            }
        }

        return errors;
    }

    /// <summary>
    /// One moving inversion: checks the expected value and writes its complement going up, then checks that going down.
    /// Faults that only show when a neighbour changes are found by the changing direction.
    /// </summary>
    public static long MovingInversion(Span<ulong> words, ulong pattern)
    {
        words.Fill(pattern);
        var errors = 0L;
        for (var index = 0; index < words.Length; index++)
        {
            if (words[index] != pattern)
            {
                errors++;
            }

            words[index] = ~pattern;
        }

        for (var index = words.Length - 1; index >= 0; index--)
        {
            if (words[index] != ~pattern)
            {
                errors++;
            }
        }

        return errors;
    }

    public static void FillRandom(Span<ulong> words, ulong seed)
    {
        var state = seed | 1;
        for (var index = 0; index < words.Length; index++)
        {
            state = NextRandom(state);
            words[index] = state;
        }
    }

    public static long CountRandomErrors(ReadOnlySpan<ulong> words, ulong seed)
    {
        var state = seed | 1;
        var errors = 0L;
        for (var index = 0; index < words.Length; index++)
        {
            state = NextRandom(state);
            if (words[index] != state)
            {
                errors++;
            }
        }

        return errors;
    }

    public static ulong PlanBytes(ulong availableBytes, double fraction) =>
        (ulong)(availableBytes * fraction) / CHUNK_BYTES * CHUNK_BYTES;

    private static ulong NextRandom(ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static void RunPattern(int pattern, ulong[][] chunks, ulong pass, ref long errors, CancellationToken stop)
    {
        var total = 0L;
        Exception? failure = null;
        var next = -1;
        var workers = Enumerable.Range(0, Math.Min(Environment.ProcessorCount, chunks.Length)).Select(_ => new Thread(() =>
        {
            try
            {
                for (var index = Interlocked.Increment(ref next); index < chunks.Length && !stop.IsCancellationRequested; index = Interlocked.Increment(ref next))
                {
                    Interlocked.Add(ref total, RunChunk(pattern, chunks[index], index, pass));
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        }).ToList();

        workers.ForEach(worker => worker.Start());
        workers.ForEach(worker => worker.Join());
        errors += total;
        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Message, failure);
        }
    }

    private static long RunChunk(int pattern, ulong[] chunk, int chunkIndex, ulong pass)
    {
        var baseAddress = ((ulong)chunkIndex * CHUNK_WORDS) + pass;
        switch (pattern)
        {
            case 0:
                FillOwnAddress(chunk, baseAddress);
                return CountOwnAddressErrors(chunk, baseAddress);
            case 1:
                return MovingInversion(chunk, pass % 2 == 0 ? 0 : ALL_ONES);
            default:
                var seed = RANDOM_SEED + baseAddress;
                FillRandom(chunk, seed);
                return CountRandomErrors(chunk, seed);
        }
    }

    private MemoryTestResult Run(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var chunks = new List<ulong[]>();
        var passes = 0L;
        var errors = 0L;
        try
        {
            var planned = PlanBytes(ReadAvailableBytes(), availableFraction);
            for (ulong allocated = 0; allocated < planned && !cancellationToken.IsCancellationRequested; allocated += CHUNK_BYTES)
            {
                try
                {
                    chunks.Add(GC.AllocateUninitializedArray<ulong>(CHUNK_WORDS));
                }
                catch (OutOfMemoryException)
                {
                    // Other programs can take memory after the plan was made. Testing less is still a valid test.
                    break;
                }
            }

            if (chunks.Count == 0)
            {
                return Finish(MemoryTestEnd.Unsupported, 0, 0, 0, clock, "Not enough free memory");
            }

            var array = chunks.ToArray();
            while (clock.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                RunPattern((int)(passes % PATTERN_COUNT), array, (ulong)passes, ref errors, cancellationToken);
                passes++;
                progress?.Report(Math.Min(1, clock.Elapsed / duration));
                if (errors > 0)
                {
                    break;
                }
            }

            var end = (errors, cancellationToken.IsCancellationRequested) switch
            {
                ( > 0, _) => MemoryTestEnd.ErrorsFound,
                (_, true) => MemoryTestEnd.Stopped,
                _ => MemoryTestEnd.Completed,
            };
            return Finish(end, (ulong)chunks.Count * CHUNK_BYTES, passes, errors, clock, null);
        }
        catch (Exception exception)
        {
            return Finish(MemoryTestEnd.Failed, (ulong)chunks.Count * CHUNK_BYTES, passes, errors, clock, exception.Message);
        }
        finally
        {
            chunks.Clear();
            GC.Collect();
        }
    }

    private static MemoryTestResult Finish(MemoryTestEnd end, ulong bytes, long passes, long errors, Stopwatch clock, string? detail) =>
        new(end, bytes, passes, errors, clock.Elapsed.TotalSeconds, detail);

    private static ulong ReadAvailableBytes()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? status.AvailablePhysical : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
