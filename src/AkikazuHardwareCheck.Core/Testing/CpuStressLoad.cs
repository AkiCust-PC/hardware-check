using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Testing;

/// <summary>
/// Loads every logical CPU with hashing and floating-point vector work, then compares each result with a known answer.
/// Workers run below normal priority so the interface and the sensor sampler stay responsive.
/// </summary>
public sealed class CpuStressLoad(int? threadCount = null) : IStressLoad
{
    private const int HASH_BUFFER_BYTES = 64 * 1024;
    private const int HASH_ROUNDS = 8;
    private const int MATRIX_SIZE = 96;
    private const int MATRIX_ROUNDS = 32;
    private const double PERCENT = 100;

    public Task<StressOutcome> RunAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var workers = Math.Max(1, threadCount ?? Environment.ProcessorCount);
        return Task.Factory.StartNew(
            () => Run(workers, duration, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>One deterministic work unit. The result depends only on the seed, whatever vector width the CPU has.</summary>
    public static ulong ComputeWorkUnit(uint seed)
    {
        var hash = HashWork(seed);
        var matrix = MatrixWork(seed);
        return hash ^ matrix;
    }

    private static StressOutcome Run(int workers, TimeSpan duration, CancellationToken cancellationToken)
    {
        // Answers are computed before any load exists, so heat cannot affect the reference values.
        var answers = Enumerable.Range(0, workers).Select(index => ComputeWorkUnit((uint)index)).ToArray();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long workUnits = 0;
        long errors = 0;
        string? failure = null;

        using var process = Process.GetCurrentProcess();
        var cpuTimeAtStart = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();

        var threads = Enumerable.Range(0, workers).Select(index => new Thread(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested && clock.Elapsed < duration)
                {
                    if (ComputeWorkUnit((uint)index) != answers[index])
                    {
                        Interlocked.Increment(ref errors);
                        stop.Cancel();
                        return;
                    }

                    Interlocked.Increment(ref workUnits);
                }
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                stop.Cancel();
            }
        })
        {
            IsBackground = true,
            Name = $"CPU stress {index}",
            Priority = ThreadPriority.BelowNormal,
        }).ToList();

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        process.Refresh();
        var measuredLoad = clock.Elapsed > TimeSpan.Zero
            ? (process.TotalProcessorTime - cpuTimeAtStart) / clock.Elapsed / Environment.ProcessorCount * PERCENT
            : (double?)null;

        var end = (errors, failure, cancellationToken.IsCancellationRequested) switch
        {
            ( > 0, _, _) => StressEnd.VerificationFailed,
            (_, not null, _) => StressEnd.Failed,
            (_, _, true) => StressEnd.Cancelled,
            _ => StressEnd.Completed,
        };

        return new StressOutcome(end, workUnits, errors, measuredLoad, failure);
    }

    private static ulong HashWork(uint seed)
    {
        var buffer = new byte[HASH_BUFFER_BYTES];
        BitConverter.TryWriteBytes(buffer, seed);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        for (var round = 0; round < HASH_ROUNDS; round++)
        {
            SHA256.HashData(buffer, digest);
            digest.CopyTo(buffer.AsSpan(round * digest.Length));
        }

        return BitConverter.ToUInt64(digest);
    }

    private static ulong MatrixWork(uint seed)
    {
        var left = FillMatrix(seed);
        var right = FillMatrix(seed + 1);
        var result = new double[MATRIX_SIZE * MATRIX_SIZE];
        for (var round = 0; round < MATRIX_ROUNDS; round++)
        {
            Multiply(left, right, result);
            (left, result) = (result, left);
        }

        var total = 0UL;
        foreach (var value in left)
        {
            total = BitOperations.RotateLeft(total, 7) ^ BitConverter.DoubleToUInt64Bits(value);
        }

        return total;
    }

    // Each element is updated independently, so results match across Vector<double> widths.
    private static void Multiply(double[] left, double[] right, double[] result)
    {
        Array.Clear(result);
        var width = Vector<double>.Count;
        for (var row = 0; row < MATRIX_SIZE; row++)
        {
            var target = result.AsSpan(row * MATRIX_SIZE, MATRIX_SIZE);
            for (var middle = 0; middle < MATRIX_SIZE; middle++)
            {
                var factor = new Vector<double>(left[(row * MATRIX_SIZE) + middle]);
                var source = right.AsSpan(middle * MATRIX_SIZE, MATRIX_SIZE);
                var column = 0;
                for (; column <= MATRIX_SIZE - width; column += width)
                {
                    (new Vector<double>(target[column..]) + (factor * new Vector<double>(source[column..]))).CopyTo(target[column..]);
                }

                for (; column < MATRIX_SIZE; column++)
                {
                    target[column] += factor[0] * source[column];
                }
            }
        }

        // Keeps values in a stable range over rounds.
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = double.Round(result[index] % 1, 12);
        }
    }

    private static double[] FillMatrix(uint seed)
    {
        var state = seed + 1;
        var matrix = new double[MATRIX_SIZE * MATRIX_SIZE];
        for (var index = 0; index < matrix.Length; index++)
        {
            state = Xorshift.Next(state);
            matrix[index] = state / (double)uint.MaxValue;
        }

        return matrix;
    }
}
