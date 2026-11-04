using System.Diagnostics;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Models;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AkikazuHardwareCheck.Core.Testing;

/// <summary>
/// Loads one GPU with a Direct3D 11 compute shader. Each batch's integer results are read back and compared with the
/// same calculation on the CPU, and any device removal is recorded rather than thrown.
/// </summary>
public sealed class GpuStressLoad(string adapterLuid) : IStressLoad
{
    private const int ELEMENT_COUNT = 1 << 20;
    private const int THREAD_GROUP_SIZE = 64;
    private const int VALUES_PER_ELEMENT = 2;
    private const int VERIFY_STRIDE = 4099;
    private const uint INITIAL_ROUNDS = 16;
    private const uint MAX_ROUNDS = 16384;

    // Windows resets a GPU after about 2 seconds of unbroken work, so batches stay far below that.
    private static readonly TimeSpan MinBatchTime = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan MaxBatchTime = TimeSpan.FromMilliseconds(500);

    private const string SHADER = """
        RWStructuredBuffer<uint> Output : register(u0);
        cbuffer Parameters : register(b0)
        {
            uint Seed;
            uint Rounds;
            uint Padding0;
            uint Padding1;
        };

        [numthreads(64, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            uint state = id.x + Seed + 1;
            float mixed = float(state & 65535) / 65536.0;
            for (uint round = 0; round < Rounds; round++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                mixed = frac(sin(mixed * 6.2831853 + float(state & 255) * 0.01) * 43758.5453);
            }

            Output[id.x * 2] = state;
            Output[id.x * 2 + 1] = asuint(mixed);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShaderParameters
    {
        public uint Seed;
        public uint Rounds;
        public uint Padding0;
        public uint Padding1;
    }

    public Task<StressOutcome> RunAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
            () => Run(duration, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>The shader's integer result, calculated on the CPU to check it independently.</summary>
    public static uint ExpectedState(int element, uint seed, uint rounds)
    {
        var state = (uint)element + seed + 1;
        for (uint round = 0; round < rounds; round++)
        {
            state = Xorshift.Next(state);
        }

        return state;
    }

    /// <summary>Doubles the rounds for short batches and halves them for long ones, keeping each batch safely short.</summary>
    public static uint NextRounds(uint rounds, TimeSpan batchTime)
    {
        if (batchTime > MaxBatchTime)
        {
            return Math.Max(1, rounds / 2);
        }

        return batchTime < MinBatchTime ? Math.Min(MAX_ROUNDS, rounds * 2) : rounds;
    }

    public static StressEnd ClassifyRemoval(int removedReason)
    {
        var code = new Result(removedReason);
        return code == Vortice.DXGI.ResultCode.DeviceReset || code == Vortice.DXGI.ResultCode.DeviceHung
            ? StressEnd.DriverReset
            : StressEnd.DeviceLost;
    }

    private StressOutcome Run(TimeSpan duration, CancellationToken cancellationToken)
    {
        ID3D11Device? device = null;
        long batches = 0;
        long errors = 0;
        try
        {
            device = CreateDevice();
            if (device is null)
            {
                return new StressOutcome(StressEnd.Unsupported, 0, 0, null, $"No Direct3D 11 device for adapter {adapterLuid}");
            }

            using var context = device.ImmediateContext;
            using var shader = CompileShader(device);
            using var output = device.CreateBuffer(new BufferDescription
            {
                ByteWidth = ELEMENT_COUNT * VALUES_PER_ELEMENT * sizeof(uint),
                BindFlags = BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = sizeof(uint),
            });
            using var readback = device.CreateBuffer(new BufferDescription
            {
                ByteWidth = ELEMENT_COUNT * VALUES_PER_ELEMENT * sizeof(uint),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
            using var parameters = device.CreateBuffer(new BufferDescription
            {
                ByteWidth = (uint)Marshal.SizeOf<ShaderParameters>(),
                BindFlags = BindFlags.ConstantBuffer,
            });
            using var view = device.CreateUnorderedAccessView(output);

            context.CSSetShader(shader);
            context.CSSetUnorderedAccessView(0, view);
            context.CSSetConstantBuffer(0, parameters);

            var rounds = INITIAL_ROUNDS;
            var clock = Stopwatch.StartNew();
            var seed = 0u;
            while (clock.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                var batchStart = clock.Elapsed;
                var data = new ShaderParameters { Seed = seed, Rounds = rounds };
                context.UpdateSubresource(in data, parameters);
                context.Dispatch(ELEMENT_COUNT / THREAD_GROUP_SIZE, 1, 1);
                context.CopyResource(readback, output);

                if (!VerifyBatch(context, readback, seed, rounds))
                {
                    errors++;
                    return new StressOutcome(StressEnd.VerificationFailed, batches, errors, null, $"Mismatch at seed {seed}, rounds {rounds}");
                }

                batches++;
                seed++;
                rounds = NextRounds(rounds, clock.Elapsed - batchStart);
            }

            var removed = device.DeviceRemovedReason;
            if (removed.Failure)
            {
                return new StressOutcome(ClassifyRemoval(removed.Code), batches, errors, null, removed.ToString());
            }

            return new StressOutcome(cancellationToken.IsCancellationRequested ? StressEnd.Cancelled : StressEnd.Completed, batches, errors, null, null);
        }
        catch (Exception exception)
        {
            var removed = device?.DeviceRemovedReason ?? Result.Ok;
            return removed.Failure
                ? new StressOutcome(ClassifyRemoval(removed.Code), batches, errors, null, removed.ToString())
                : new StressOutcome(StressEnd.Failed, batches, errors, null, exception.Message);
        }
        finally
        {
            device?.Dispose();
        }
    }

    private static bool VerifyBatch(ID3D11DeviceContext context, ID3D11Buffer readback, uint seed, uint rounds)
    {
        var mapped = context.Map(readback, MapMode.Read);
        try
        {
            for (var element = 0; element < ELEMENT_COUNT; element += VERIFY_STRIDE)
            {
                var actual = (uint)Marshal.ReadInt32(mapped.DataPointer, element * VALUES_PER_ELEMENT * sizeof(uint));
                if (actual != ExpectedState(element, seed, rounds))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            context.Unmap(readback);
        }
    }

    private static ID3D11ComputeShader CompileShader(ID3D11Device device)
    {
        var compiled = Compiler.Compile(SHADER, "main", "stress.hlsl", "cs_5_0");
        return device.CreateComputeShader(compiled.Span);
    }

    private ID3D11Device? CreateDevice()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
        {
            using (adapter)
            {
                var description = adapter.Description1;
                if (GpuInfoCollector.FormatLuid(description.Luid.HighPart, description.Luid.LowPart) != adapterLuid)
                {
                    continue;
                }

                var result = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, null, out ID3D11Device? device);
                return result.Success ? device : null;
            }
        }

        return null;
    }
}
