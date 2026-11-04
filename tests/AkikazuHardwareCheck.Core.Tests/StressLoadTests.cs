using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Testing;
using Vortice.DXGI;

namespace AkikazuHardwareCheck.Core.Tests;

public class StressLoadTests
{
    [Fact]
    public void WorkUnitIsDeterministicAcrossThreads()
    {
        var expected = CpuStressLoad.ComputeWorkUnit(7);
        var results = new ulong[4];

        Parallel.For(0, results.Length, index => results[index] = CpuStressLoad.ComputeWorkUnit(7));

        Assert.All(results, result => Assert.Equal(expected, result));
    }

    [Fact]
    public void WorkUnitDependsOnSeed()
    {
        Assert.NotEqual(CpuStressLoad.ComputeWorkUnit(1), CpuStressLoad.ComputeWorkUnit(2));
    }

    [Fact]
    public async Task CpuLoadCompletesAndMeasuresItself()
    {
        var outcome = await new CpuStressLoad(threadCount: 1).RunAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        Assert.Equal(StressEnd.Completed, outcome.End);
        Assert.True(outcome.WorkUnits > 0);
        Assert.Equal(0, outcome.VerificationErrors);
        Assert.NotNull(outcome.MeasuredLoadPercent);
    }

    [Fact]
    public async Task CpuLoadStopsOnCancel()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = new CpuStressLoad(threadCount: 1).RunAsync(TimeSpan.FromMinutes(5), stop.Token);

        stop.CancelAfter(TimeSpan.FromMilliseconds(50));
        var outcome = await task;

        Assert.Equal(StressEnd.Cancelled, outcome.End);
    }

    [Fact]
    public void ExpectedShaderStateMatchesTheXorshiftDefinition()
    {
        // state = 0 + 0 + 1, then one round of xorshift32 (13, 17, 5).
        Assert.Equal(270369u, GpuStressLoad.ExpectedState(0, 0, 1));
        Assert.Equal(1u, GpuStressLoad.ExpectedState(0, 0, 0));
    }

    [Theory]
    [InlineData(16, 10, 32)]
    [InlineData(16, 300, 16)]
    [InlineData(16, 900, 8)]
    [InlineData(1, 900, 1)]
    [InlineData(16384, 10, 16384)]
    public void BatchSizeAdaptsToStayBelowTheDriverResetLimit(uint rounds, int milliseconds, uint expected)
    {
        Assert.Equal(expected, GpuStressLoad.NextRounds(rounds, TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void RemovalReasonsDistinguishDriverResetFromDeviceLoss()
    {
        Assert.Equal(StressEnd.DriverReset, GpuStressLoad.ClassifyRemoval(ResultCode.DeviceReset.Code));
        Assert.Equal(StressEnd.DriverReset, GpuStressLoad.ClassifyRemoval(ResultCode.DeviceHung.Code));
        Assert.Equal(StressEnd.DeviceLost, GpuStressLoad.ClassifyRemoval(ResultCode.DeviceRemoved.Code));
    }

    [Fact]
    public async Task UnknownAdapterIsUnsupportedRatherThanACrash()
    {
        var outcome = await new GpuStressLoad("FFFFFFFF:FFFFFFFF").RunAsync(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(StressEnd.Unsupported, outcome.End);
    }
}
