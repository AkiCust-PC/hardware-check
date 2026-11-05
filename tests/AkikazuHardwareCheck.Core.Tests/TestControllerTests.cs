using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;
using AkikazuHardwareCheck.Core.Testing;

namespace AkikazuHardwareCheck.Core.Tests;

public class TestControllerTests
{
    private const double SHORT_PHASE_SECONDS = 0.3;
    private const double SAMPLE_SECONDS = 0.02;

    private static readonly GpuInfo Gpu = new("NVIDIA", "RTX", 1UL << 33, "1.0", GpuKind.Discrete, "0:1");

    private static readonly TestProfile Profile = new(SHORT_PHASE_SECONDS, SHORT_PHASE_SECONDS, SHORT_PHASE_SECONDS, SAMPLE_SECONDS, 100, 95);

    private static TestPlan FullPlan => TestPlan.Full(Profile, Gpu);

    private static StressOutcome Completed(double load = 95) => new(StressEnd.Completed, 10, 0, load, null);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<TestRunResult> Run(FakeSensors sensors, IStressLoad gpu, IStressLoad cpu, TestPlan? plan = null, bool confirm = false, CancellationToken? token = null)
    {
        var controller = new TestController(sensors, _ => gpu, cpu)
        {
            ConfirmUnmonitored = (_, _) => Task.FromResult(confirm),
            SensorLostAfter = TimeSpan.FromMilliseconds(100),
            WatchdogGrace = TimeSpan.FromMilliseconds(100),
        };
        return await controller.RunAsync(plan ?? FullPlan, token ?? Token);
    }

    private static PhaseResult Phase(TestRunResult result, TestPhase phase) => result.Phases.Single(item => item.Phase == phase);

    [Fact]
    public async Task HealthyRunPassesAndKeepsPhasesInOrder()
    {
        var order = new List<string>();
        var result = await Run(new FakeSensors(), new FakeLoad(Completed(), () => order.Add("gpu")), new FakeLoad(Completed(), () => order.Add("cpu")));

        Assert.Equal(OverallResult.NoObviousIssue, result.Overall);
        Assert.Equal(["gpu", "cpu"], order);
        Assert.Equal([TestPhase.Idle, TestPhase.GpuStress, TestPhase.CpuStress], result.Phases.Select(phase => phase.Phase));
        Assert.All(result.Phases, phase => Assert.Equal(TestStatus.Passed, phase.Status));
        Assert.Empty(result.Anomalies);
    }

    [Fact]
    public async Task BothLoadsRunAtTheSameTimeForTheLongerDuration()
    {
        var cpuStarted = new TaskCompletionSource();
        var gpu = new FakeLoad(Completed()) { WaitBeforeFinishing = cpuStarted.Task };
        var cpu = new FakeLoad(Completed(), cpuStarted.SetResult);
        var plan = TestPlan.Full(Profile with { CpuStressSeconds = 0.5 }, Gpu);

        var result = await Run(new FakeSensors(), gpu, cpu, plan);

        Assert.Equal(TimeSpan.FromSeconds(0.5), gpu.RequestedDuration);
        Assert.Equal(TimeSpan.FromSeconds(0.5), cpu.RequestedDuration);
        Assert.Equal(TestStatus.Passed, Phase(result, TestPhase.GpuStress).Status);
        Assert.Equal(TestStatus.Passed, Phase(result, TestPhase.CpuStress).Status);
    }

    [Fact]
    public async Task OverheatingOneDeviceStopsBothLoads()
    {
        var sensors = new FakeSensors();
        var gpu = new FakeLoad(Completed()) { WaitsForCancel = true };
        var cpu = new FakeLoad(Completed(), () => sensors.CpuTemperature = 105) { WaitsForCancel = true };

        var result = await Run(sensors, gpu, cpu);

        Assert.True(gpu.WasCancelled);
        Assert.True(cpu.WasCancelled);
        Assert.Equal(TestStatus.IssueFound, Phase(result, TestPhase.CpuStress).Status);
        Assert.Equal(TestStatus.Incomplete, Phase(result, TestPhase.GpuStress).Status);
        Assert.Equal(OverallResult.NeedsReview, result.Overall);
    }

    [Fact]
    public async Task StatisticsSeparatePhasesAndSensors()
    {
        var sensors = new FakeSensors();
        var cpu = new FakeLoad(Completed(), () => sensors.CpuTemperature = 80);

        var result = await Run(sensors, new FakeLoad(Completed()), cpu);

        var idle = result.Statistics.Single(item => item is { Phase: TestPhase.Idle, SensorName: "CPU Package" });
        var stress = result.Statistics.Single(item => item is { Phase: TestPhase.DualStress, SensorName: "CPU Package" });
        Assert.Equal(40, idle.Maximum);
        Assert.Equal(80, stress.Maximum);
        Assert.True(idle.ValidSamples > 1);
    }

    [Fact]
    public async Task NoCpuTemperatureIsNeverReportedAsPassed()
    {
        var sensors = new FakeSensors { CpuTemperature = null };

        var result = await Run(sensors, new FakeLoad(Completed()), new FakeLoad(Completed()));

        Assert.Equal(TestStatus.InsufficientData, Phase(result, TestPhase.CpuStress).Status);
        Assert.Equal(OverallResult.Incomplete, result.Overall);
        Assert.Contains(result.Anomalies, anomaly => anomaly is { Code: AnomalyCode.TemperatureUnavailable, Phase: TestPhase.CpuStress });
    }

    [Fact]
    public async Task UnmonitoredDeviceIsSkippedWithoutConfirmation()
    {
        var sensors = new FakeSensors { CpuTemperature = null };
        var cpu = new FakeLoad(Completed());

        await Run(sensors, new FakeLoad(Completed()), cpu);

        Assert.False(cpu.WasRun);
    }

    [Fact]
    public async Task ConfirmedUnmonitoredRunStillCannotPassAndIsCapped()
    {
        var sensors = new FakeSensors { CpuTemperature = null };
        var cpu = new FakeLoad(Completed());
        var plan = FullPlan with { RunIdle = false, RunGpu = false, Profile = Profile with { CpuStressSeconds = 300 } };
        cpu.DurationOverride = TimeSpan.FromMilliseconds(50);

        var result = await Run(sensors, new FakeLoad(Completed()), cpu, plan, confirm: true);

        Assert.True(cpu.WasRun);
        Assert.Equal(TimeSpan.FromSeconds(60), cpu.RequestedDuration);
        Assert.Equal(TestStatus.InsufficientData, Phase(result, TestPhase.CpuStress).Status);
        Assert.Equal(OverallResult.Incomplete, result.Overall);
    }

    [Fact]
    public async Task OverheatingStopsTheLoadAndIsAnIssue()
    {
        var sensors = new FakeSensors();
        var cpu = new FakeLoad(Completed(), () => sensors.CpuTemperature = 105) { WaitsForCancel = true };

        var result = await Run(sensors, new FakeLoad(Completed()), cpu);

        Assert.Equal(TestStatus.IssueFound, Phase(result, TestPhase.CpuStress).Status);
        Assert.Equal(OverallResult.NeedsReview, result.Overall);
        Assert.Contains(result.Anomalies, anomaly => anomaly.Code == AnomalyCode.TemperatureProtection);
        Assert.True(cpu.WasCancelled);
    }

    [Fact]
    public async Task LosingTheSensorStopsTheLoadButIsNotAHardwareFault()
    {
        var sensors = new FakeSensors();
        var profile = Profile with { CpuStressSeconds = 20 };
        var cpu = new FakeLoad(Completed(), () => sensors.CpuTemperature = null) { WaitsForCancel = true };

        var result = await Run(sensors, new FakeLoad(Completed()), cpu, TestPlan.Full(profile, Gpu));

        Assert.Contains(result.Anomalies, anomaly => anomaly.Code == AnomalyCode.SensorLost);
        Assert.Equal(TestStatus.Incomplete, Phase(result, TestPhase.CpuStress).Status);
        Assert.Equal(OverallResult.Incomplete, result.Overall);
    }

    [Theory]
    [InlineData(StressEnd.DeviceLost, AnomalyCode.GpuDeviceLost, TestStatus.IssueFound, OverallResult.NeedsReview)]
    [InlineData(StressEnd.DriverReset, AnomalyCode.GpuDriverReset, TestStatus.IssueFound, OverallResult.NeedsReview)]
    [InlineData(StressEnd.VerificationFailed, AnomalyCode.GpuVerificationError, TestStatus.IssueFound, OverallResult.NeedsReview)]
    [InlineData(StressEnd.Failed, AnomalyCode.StressAbnormalExit, TestStatus.ExecutionFailed, OverallResult.Incomplete)]
    public async Task GpuFailureIsRecordedAndCpuLoadStillFinishes(StressEnd end, AnomalyCode expected, TestStatus phaseStatus, OverallResult overall)
    {
        var cpu = new FakeLoad(Completed());

        var result = await Run(new FakeSensors(), new FakeLoad(new StressOutcome(end, 1, 0, null, "detail")), cpu);

        Assert.Contains(result.Anomalies, anomaly => anomaly.Code == expected);
        Assert.Equal(phaseStatus, Phase(result, TestPhase.GpuStress).Status);
        Assert.True(cpu.WasRun);
        Assert.Equal(TestStatus.Passed, Phase(result, TestPhase.CpuStress).Status);
        Assert.Equal(overall, result.Overall);
    }

    [Fact]
    public async Task CpuVerificationErrorIsAnIssue()
    {
        var result = await Run(new FakeSensors(), new FakeLoad(Completed()), new FakeLoad(new StressOutcome(StressEnd.VerificationFailed, 1, 1, 90, null)));

        Assert.Contains(result.Anomalies, anomaly => anomaly.Code == AnomalyCode.CpuVerificationError);
        Assert.Equal(OverallResult.NeedsReview, result.Overall);
    }

    [Fact]
    public async Task LoadBelowTargetIsAnIssue()
    {
        var sensors = new FakeSensors { CpuLoad = null };

        var result = await Run(sensors, new FakeLoad(Completed()), new FakeLoad(Completed(load: 20)));

        Assert.Contains(result.Anomalies, anomaly => anomaly is { Code: AnomalyCode.LoadBelowTarget, Phase: TestPhase.CpuStress });
    }

    [Fact]
    public async Task UnknownGpuLoadIsNotAPass()
    {
        var sensors = new FakeSensors { GpuLoad = null };

        var result = await Run(sensors, new FakeLoad(new StressOutcome(StressEnd.Completed, 5, 0, null, null)), new FakeLoad(Completed()));

        Assert.Contains(result.Anomalies, anomaly => anomaly.Code == AnomalyCode.LoadUnverified);
        Assert.Equal(TestStatus.InsufficientData, Phase(result, TestPhase.GpuStress).Status);
    }

    [Fact]
    public async Task BusyIdleIsNotReportedAsNormalIdle()
    {
        var sensors = new FakeSensors { CpuLoad = 90 };

        var result = await Run(sensors, new FakeLoad(Completed()), new FakeLoad(Completed()));

        Assert.Contains(result.Anomalies, anomaly => anomaly.Code == AnomalyCode.IdleConditionNotMet);
        Assert.Equal(TestStatus.Incomplete, Phase(result, TestPhase.Idle).Status);
    }

    [Fact]
    public async Task UserStopEndsBothLoads()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var cpu = new FakeLoad(Completed()) { WaitsForCancel = true };
        var gpu = new FakeLoad(Completed(), stop.Cancel) { WaitsForCancel = true };

        var result = await Run(new FakeSensors(), gpu, cpu, token: stop.Token);

        Assert.Equal(TestStatus.Cancelled, Phase(result, TestPhase.GpuStress).Status);
        Assert.Equal(TestStatus.Cancelled, Phase(result, TestPhase.CpuStress).Status);
        Assert.Contains(result.Anomalies, anomaly => anomaly.Code == AnomalyCode.UserStopped);
        Assert.Equal(OverallResult.Incomplete, result.Overall);
    }

    [Fact]
    public async Task NoGpuMeansUnsupportedRatherThanPassed()
    {
        var result = await Run(new FakeSensors(), new FakeLoad(Completed()), new FakeLoad(Completed()), TestPlan.Full(Profile, null));

        Assert.Equal(TestStatus.Unsupported, Phase(result, TestPhase.GpuStress).Status);
        Assert.Equal(OverallResult.Incomplete, result.Overall);
    }

    [Fact]
    public async Task SingleTestIgnoresPhasesThatWereNotRequested()
    {
        var plan = new TestPlan(Profile, false, false, true, Gpu);

        var result = await Run(new FakeSensors(), new FakeLoad(Completed()), new FakeLoad(Completed()), plan);

        Assert.False(Phase(result, TestPhase.GpuStress).IsRequested);
        Assert.Equal(OverallResult.NoObviousIssue, result.Overall);
    }

    [Fact]
    public async Task SensorReadFailuresDoNotCrashTheRun()
    {
        var sensors = new FakeSensors { FailsReads = true };

        var result = await Run(sensors, new FakeLoad(Completed()), new FakeLoad(Completed()));

        Assert.True(result.SensorReadFailures > 0);
        Assert.Equal(OverallResult.Incomplete, result.Overall);
    }

    [Fact]
    public async Task AHungLoadCannotHangTheSequence()
    {
        var result = await Run(new FakeSensors(), new HungLoad(), new FakeLoad(Completed()));

        Assert.Contains(result.Anomalies, anomaly => anomaly is { Code: AnomalyCode.StressTimedOut, Phase: TestPhase.GpuStress });
        Assert.Equal(TestStatus.ExecutionFailed, Phase(result, TestPhase.GpuStress).Status);
        Assert.Equal(OverallResult.Incomplete, result.Overall);
    }

    [Fact]
    public async Task ProgressCarriesLiveValues()
    {
        var seen = new List<TestProgress>();
        var controller = new TestController(new FakeSensors(), _ => new FakeLoad(Completed()), new FakeLoad(Completed()));
        controller.ProgressChanged += progress =>
        {
            lock (seen)
            {
                seen.Add(progress);
            }
        };

        await controller.RunAsync(FullPlan, Token);

        Assert.Contains(seen, progress => progress is { Phase: TestPhase.Idle, CpuTemperature: 40, GpuTemperature: 50 });
    }

    [Theory]
    [InlineData(StressEnd.Completed, true, true, true, false, TestStatus.Passed)]
    [InlineData(StressEnd.Completed, false, true, true, false, TestStatus.InsufficientData)]
    [InlineData(StressEnd.Completed, true, false, false, false, TestStatus.InsufficientData)]
    [InlineData(StressEnd.Completed, true, true, false, false, TestStatus.IssueFound)]
    [InlineData(StressEnd.Completed, true, true, true, true, TestStatus.IssueFound)]
    [InlineData(StressEnd.Cancelled, true, true, true, false, TestStatus.Cancelled)]
    [InlineData(StressEnd.Failed, true, true, true, false, TestStatus.ExecutionFailed)]
    [InlineData(StressEnd.Unsupported, false, false, false, false, TestStatus.Unsupported)]
    [InlineData(StressEnd.DeviceLost, false, false, false, false, TestStatus.IssueFound)]
    public void StressStatusRules(StressEnd end, bool hasTemperature, bool isLoadKnown, bool isLoadSufficient, bool wasOverheated, TestStatus expected)
    {
        Assert.Equal(expected, TestResultEvaluator.EvaluateStress(end, hasTemperature, isLoadKnown, isLoadSufficient, wasOverheated));
    }

    [Fact]
    public void NoRequestedPhasesIsNotAPass()
    {
        Assert.Equal(OverallResult.Incomplete, TestResultEvaluator.Evaluate([]));
    }

    [Fact]
    public void ProfileClampsStressDurations()
    {
        var clamped = (TestProfile.Standard with { GpuStressSeconds = 5, CpuStressSeconds = 1000 }).ClampedToSupportedRange();

        Assert.Equal(60, clamped.GpuStressSeconds);
        Assert.Equal(300, clamped.CpuStressSeconds);
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryResultCodeHasText(string language)
    {
        var t = Translator.Load(language);
        foreach (var value in Enum.GetValues<TestPhase>())
        {
            Assert.NotEmpty(EnumText.Phase(t, value));
        }

        foreach (var value in Enum.GetValues<TestStatus>())
        {
            Assert.NotEmpty(EnumText.Status(t, value));
        }

        foreach (var value in Enum.GetValues<OverallResult>())
        {
            Assert.NotEmpty(EnumText.Overall(t, value));
        }

        foreach (var value in Enum.GetValues<AnomalyCode>())
        {
            Assert.NotEmpty(EnumText.Anomaly(t, value));
        }
    }

    private sealed class FakeSensors : ISensorSource
    {
        public double? CpuTemperature { get; set; } = 40;

        public double? GpuTemperatureValue { get; set; } = 50;

        // No CPU load sensor by default, so the load's own measurement decides. The sensor cannot follow fake loads.
        public double? CpuLoad { get; set; }

        public double? GpuLoad { get; set; } = 90;

        public bool FailsReads { get; init; }

        public IReadOnlyList<SensorReading> Read()
        {
            if (FailsReads)
            {
                throw new InvalidOperationException("no sensors");
            }

            var readings = new List<SensorReading>();
            Add(readings, "Cpu", "CPU", "CPU Package", "Temperature", CpuTemperature);
            Add(readings, "Cpu", "CPU", "CPU Total", "Load", CpuLoad);
            Add(readings, "GpuNvidia", "RTX", "GPU Core", "Temperature", GpuTemperatureValue);
            Add(readings, "GpuNvidia", "RTX", "GPU Core", "Load", GpuLoad);
            return readings;
        }

        public void Dispose()
        {
        }

        private static void Add(List<SensorReading> readings, string hardwareType, string hardware, string name, string type, double? value)
        {
            if (value is not null)
            {
                readings.Add(new SensorReading($"/{hardware}", hardware, hardwareType, $"/{hardware}/{type}/{name}", name, type, value, "", SensorStatus.Ok, "test", DateTimeOffset.Now));
            }
        }
    }

    private sealed class HungLoad : IStressLoad
    {
        public Task<StressOutcome> RunAsync(TimeSpan duration, CancellationToken cancellationToken) => new TaskCompletionSource<StressOutcome>().Task;
    }

    private sealed class FakeLoad(StressOutcome outcome, Action? onStart = null) : IStressLoad
    {
        public bool WaitsForCancel { get; init; }

        public bool WasRun { get; private set; }

        public bool WasCancelled { get; private set; }

        public TimeSpan RequestedDuration { get; private set; }

        public TimeSpan? DurationOverride { get; set; }

        public Task WaitBeforeFinishing { get; init; } = Task.CompletedTask;

        public async Task<StressOutcome> RunAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            WasRun = true;
            RequestedDuration = duration;
            onStart?.Invoke();
            try
            {
                await Task.Delay(WaitsForCancel ? TimeSpan.FromSeconds(30) : DurationOverride ?? duration, cancellationToken);
                await WaitBeforeFinishing.WaitAsync(cancellationToken);
                return outcome;
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                return outcome with { End = StressEnd.Cancelled };
            }
        }
    }
}
