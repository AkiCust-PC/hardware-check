using System.Diagnostics;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.Core.Testing;

/// <summary>
/// Runs idle sampling, then the requested loads, then the summary. With both requested they run at the same time,
/// because power supply and cooling faults often show only under the combined draw.
/// The controller owns safety: temperature protection, sensor loss, the watchdog and the stop request all end loads here.
/// One instance runs one test.
/// </summary>
public sealed class TestController(
    ISensorSource sensorSource,
    Func<GpuInfo, IStressLoad> gpuLoadFactory,
    IStressLoad cpuLoad)
{
    // Sensors can take several reads to start reporting, and long-running loads stop on real sensor loss only.
    private const int INITIAL_READS = 2;
    private const double INITIALIZATION_TIMEOUT_SECONDS = 30;

    // A busy machine cannot give idle temperatures.
    private const double IDLE_MAX_CPU_LOAD_PERCENT = 30;

    // The test checks function rather than peak power, so these only catch a load that never really ran.
    private const double MIN_CPU_LOAD_PERCENT = 70;
    private const double MIN_GPU_LOAD_PERCENT = 50;

    // Without temperature data, a confirmed run stays at the shortest supported duration.
    private const double UNMONITORED_MAX_SECONDS = 60;

    private readonly Lock _gate = new();
    private readonly List<Anomaly> _anomalies = [];
    private readonly List<PhaseResult> _phases = [];
    private readonly Dictionary<TestPhase, double> _phaseSeconds = [];

    /// <summary>A load stops when its temperature sensor has been silent this long.</summary>
    public TimeSpan SensorLostAfter { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Time allowed beyond the planned duration before a load counts as hung.</summary>
    public TimeSpan WatchdogGrace { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Raised about once per sample with the latest values.</summary>
    public event Action<TestProgress>? ProgressChanged;

    /// <summary>Asked before loading a device with no temperature reading. No handler or false means it is skipped.</summary>
    public Func<TestPhase, CancellationToken, Task<bool>>? ConfirmUnmonitored { get; init; }

    public async Task<TestRunResult> RunAsync(TestPlan plan, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var session = new MonitoringSession();
        var interval = TimeSpan.FromSeconds(plan.Profile.SampleIntervalSeconds);
        using var sampler = new SensorSampler(sensorSource, session, interval);
        using var monitor = new PhaseMonitor(this, session, plan);
        sampler.Sampled += monitor.OnSampled;
        sampler.Start();

        try
        {
            await InitializeAsync(session, monitor, cancellationToken);
            await RunIdleAsync(plan, session, monitor, cancellationToken);
            await RunLoadsAsync(plan, session, monitor, cancellationToken);
            session.BeginPhase(TestPhase.Finished);
        }
        finally
        {
            sampler.Stop();
        }

        return BuildResult(plan, session, startedAt, interval);
    }

    private TestRunResult BuildResult(TestPlan plan, MonitoringSession session, DateTimeOffset startedAt, TimeSpan interval)
    {
        var statistics = SensorStatisticsCalculator.Calculate(session.Samples(), _phaseSeconds, interval);
        return new TestRunResult(
            plan.Profile,
            startedAt,
            DateTimeOffset.Now,
            TestResultEvaluator.Evaluate(_phases),
            [.. _phases],
            statistics,
            [.. _anomalies],
            SensorStatisticsCalculator.AVERAGE_METHOD,
            session.ReadCount,
            session.ReadFailures);
    }

    private async Task InitializeAsync(MonitoringSession session, PhaseMonitor monitor, CancellationToken cancellationToken)
    {
        monitor.Begin(TestPhase.Initialization, INITIALIZATION_TIMEOUT_SECONDS);
        var clock = Stopwatch.StartNew();
        while ((session.ReadCount + session.ReadFailures) < INITIAL_READS
            && clock.Elapsed.TotalSeconds < INITIALIZATION_TIMEOUT_SECONDS
            && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);
        }
    }

    private async Task RunIdleAsync(TestPlan plan, MonitoringSession session, PhaseMonitor monitor, CancellationToken cancellationToken)
    {
        if (!plan.RunIdle)
        {
            AddSkipped(TestPhase.Idle, TestStatus.NotRun, isRequested: false, plan.Profile.IdleSeconds);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            AddStopped(TestPhase.Idle, plan.Profile.IdleSeconds);
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var clock = Stopwatch.StartNew();
        monitor.Begin(TestPhase.Idle, plan.Profile.IdleSeconds);
        await DelayUntilCancelled(TimeSpan.FromSeconds(plan.Profile.IdleSeconds), cancellationToken);
        var seconds = clock.Elapsed.TotalSeconds;
        _phaseSeconds[TestPhase.Idle] = seconds;

        var samples = PhaseSamples(session, TestPhase.Idle);
        var cpuLoad = SensorStats(samples, CpuLoadSelector);
        var hasTemperature = HasSufficient(samples, plan, seconds, CpuTemperatureSelector) || HasSufficient(samples, plan, seconds, GpuTemperatureSelector(plan));
        var isStopped = cancellationToken.IsCancellationRequested;
        var isLoadKnown = cpuLoad.Average is not null;
        var isBusy = cpuLoad.Average is { } average && average > IDLE_MAX_CPU_LOAD_PERCENT;
        if (isBusy)
        {
            AddAnomaly(AnomalyCode.IdleConditionNotMet, TestPhase.Idle, FormattableString.Invariant($"{cpuLoad.Average:0.#} %"));
        }

        if (!isLoadKnown && !isStopped)
        {
            AddAnomaly(AnomalyCode.LoadUnverified, TestPhase.Idle, null);
        }

        if (!hasTemperature && !isStopped)
        {
            AddAnomaly(AnomalyCode.InsufficientSamples, TestPhase.Idle, null);
        }

        if (isStopped)
        {
            AddAnomaly(AnomalyCode.UserStopped, TestPhase.Idle, null);
        }

        var status = (isStopped, isBusy, hasTemperature, isLoadKnown) switch
        {
            (true, _, _, _) => TestStatus.Cancelled,
            (_, true, _, _) => TestStatus.Incomplete,
            (_, _, false, _) => TestStatus.InsufficientData,
            (_, _, _, false) => TestStatus.InsufficientData,
            _ => TestStatus.Passed,
        };

        _phases.Add(new PhaseResult(TestPhase.Idle, status, true, startedAt, DateTimeOffset.Now, plan.Profile.IdleSeconds, seconds, null, cpuLoad.Average, cpuLoad.Maximum, cpuLoad.Average is null ? null : "Sensor", null, 0, 0));
    }

    private async Task RunLoadsAsync(TestPlan plan, MonitoringSession session, PhaseMonitor monitor, CancellationToken cancellationToken)
    {
        var running = new List<(StressContext Stress, bool IsMonitored)>();
        foreach (var stress in RequestedStresses(plan))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                AddStopped(stress.Phase, stress.PlannedSeconds);
                continue;
            }

            var isMonitored = session.Latest.Any(stress.IsTemperatureValid);
            if (!isMonitored)
            {
                var isConfirmed = ConfirmUnmonitored is { } confirm && await confirm(stress.Phase, cancellationToken);
                AddAnomaly(AnomalyCode.TemperatureUnavailable, stress.Phase, null);
                if (!isConfirmed)
                {
                    AddSkipped(stress.Phase, TestStatus.InsufficientData, isRequested: true, stress.PlannedSeconds);
                    continue;
                }
            }

            running.Add((stress, isMonitored));
        }

        if (running.Count == 0)
        {
            return;
        }

        // Loads share one end time, so an unmonitored device caps the whole run.
        var duration = running.Max(item => item.Stress.PlannedSeconds);
        if (running.Any(item => !item.IsMonitored))
        {
            duration = Math.Min(duration, UNMONITORED_MAX_SECONDS);
        }

        var samplePhase = running.Count > 1 ? TestPhase.DualStress : running[0].Stress.Phase;
        using var safetyStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var protections = running.Select(item => new ProtectionWatch(item.Stress, safetyStop, item.IsMonitored, SensorLostAfter)).ToList();
        var startedAt = DateTimeOffset.Now;
        var clock = Stopwatch.StartNew();
        monitor.Begin(samplePhase, duration, protections);

        var outcomes = await Task.WhenAll(running.Select(item => RunWithWatchdog(item.Stress, duration, safetyStop)));
        monitor.EndStress();
        var seconds = clock.Elapsed.TotalSeconds;
        _phaseSeconds[samplePhase] = seconds;

        for (var index = 0; index < running.Count; index++)
        {
            var stress = running[index].Stress;
            RecordProtection(stress, protections[index]);
            RecordOutcome(stress.Phase, outcomes[index], cancellationToken);
            _phases.Add(SummarizeStress(stress, plan, session, samplePhase, outcomes[index], protections[index], startedAt, duration, seconds, cancellationToken));
        }
    }

    private List<StressContext> RequestedStresses(TestPlan plan)
    {
        var stresses = new List<StressContext>();
        var gpuSeconds = plan.Profile.GpuStressSeconds;
        if (!plan.RunGpu)
        {
            AddSkipped(TestPhase.GpuStress, TestStatus.NotRun, isRequested: false, gpuSeconds);
        }
        else if (plan.TargetGpu is null)
        {
            AddAnomaly(AnomalyCode.GpuUnsupported, TestPhase.GpuStress, null);
            AddSkipped(TestPhase.GpuStress, TestStatus.Unsupported, isRequested: true, gpuSeconds);
        }
        else
        {
            stresses.Add(new StressContext(TestPhase.GpuStress, gpuSeconds, plan.TargetGpu.Model, MIN_GPU_LOAD_PERCENT, GpuTemperatureSelector(plan), GpuLoadSelector(plan), plan.Profile.GpuProtectionCelsius, gpuLoadFactory(plan.TargetGpu)));
        }

        if (plan.RunCpu)
        {
            stresses.Add(new StressContext(TestPhase.CpuStress, plan.Profile.CpuStressSeconds, null, MIN_CPU_LOAD_PERCENT, CpuTemperatureSelector, CpuLoadSelector, plan.Profile.CpuProtectionCelsius, cpuLoad));
        }
        else
        {
            AddSkipped(TestPhase.CpuStress, TestStatus.NotRun, isRequested: false, plan.Profile.CpuStressSeconds);
        }

        return stresses;
    }

    private PhaseResult SummarizeStress(StressContext stress, TestPlan plan, MonitoringSession session, TestPhase samplePhase, StressOutcome outcome, ProtectionWatch protection, DateTimeOffset startedAt, double duration, double seconds, CancellationToken cancellationToken)
    {
        var samples = PhaseSamples(session, samplePhase);
        var hasTemperature = HasSufficient(samples, plan, seconds, stress.IsTemperatureValid);
        var sensorLoad = SensorStats(samples, stress.IsLoadValid);
        var loadAverage = sensorLoad.Average ?? outcome.MeasuredLoadPercent;
        var loadSource = sensorLoad.Average is not null ? "Sensor" : outcome.MeasuredLoadPercent is not null ? "ProcessTime" : null;
        var isLoadKnown = loadAverage is not null;
        var isLoadSufficient = loadAverage >= stress.MinLoadPercent;
        var ranToEnd = outcome.End == StressEnd.Completed && !protection.WasTriggered;

        if (ranToEnd)
        {
            if (isLoadKnown && !isLoadSufficient)
            {
                AddAnomaly(AnomalyCode.LoadBelowTarget, stress.Phase, FormattableString.Invariant($"{loadAverage:0.#} %"));
            }

            if (!isLoadKnown)
            {
                AddAnomaly(AnomalyCode.LoadUnverified, stress.Phase, null);
            }

            if (!hasTemperature)
            {
                AddAnomaly(AnomalyCode.InsufficientSamples, stress.Phase, null);
            }
        }

        var status = TestResultEvaluator.EvaluateStress(outcome.End, hasTemperature, isLoadKnown, isLoadSufficient, protection.WasOverheated);

        // The same StressEnd.Cancelled is used for a user stop and for a safety stop. A safety stop is an unfinished
        // measurement rather than a user cancellation, so it keeps the older "incomplete" meaning.
        if (outcome.End == StressEnd.Cancelled && !cancellationToken.IsCancellationRequested && !protection.WasOverheated)
        {
            status = TestStatus.Incomplete;
        }

        var sensorName = samples.FirstOrDefault(stress.IsTemperatureValid)?.SensorName;
        return new PhaseResult(stress.Phase, status, true, startedAt, DateTimeOffset.Now, duration, seconds, stress.TargetDevice, loadAverage, sensorLoad.Maximum, loadSource, sensorName, outcome.WorkUnits, outcome.VerificationErrors);
    }

    private async Task<StressOutcome> RunWithWatchdog(StressContext stress, double duration, CancellationTokenSource safetyStop)
    {
        var loadTask = stress.Load.RunAsync(TimeSpan.FromSeconds(duration), safetyStop.Token);
        var limit = TimeSpan.FromSeconds(duration) + WatchdogGrace;
        var finished = await Task.WhenAny(loadTask, Task.Delay(limit, CancellationToken.None));
        if (finished == loadTask)
        {
            return await loadTask;
        }

        await safetyStop.CancelAsync();
        var afterCancel = await Task.WhenAny(loadTask, Task.Delay(WatchdogGrace, CancellationToken.None));
        return afterCancel == loadTask
            ? (await loadTask) with { End = StressEnd.Failed, Detail = "Watchdog timeout" }
            : new StressOutcome(StressEnd.Failed, 0, 0, null, "Watchdog timeout: load did not stop");
    }

    private void RecordProtection(StressContext stress, ProtectionWatch protection)
    {
        if (protection.WasOverheated)
        {
            AddAnomaly(AnomalyCode.TemperatureProtection, stress.Phase, FormattableString.Invariant($"{protection.TriggerValue:0.#} °C"));
        }

        if (protection.WasSensorLost)
        {
            AddAnomaly(AnomalyCode.SensorLost, stress.Phase, null);
        }
    }

    private void RecordOutcome(TestPhase phase, StressOutcome outcome, CancellationToken cancellationToken)
    {
        switch (outcome.End)
        {
            case StressEnd.VerificationFailed:
                AddAnomaly(phase == TestPhase.CpuStress ? AnomalyCode.CpuVerificationError : AnomalyCode.GpuVerificationError, phase, outcome.Detail);
                break;
            case StressEnd.DeviceLost:
                AddAnomaly(AnomalyCode.GpuDeviceLost, phase, outcome.Detail);
                break;
            case StressEnd.DriverReset:
                AddAnomaly(AnomalyCode.GpuDriverReset, phase, outcome.Detail);
                break;
            case StressEnd.Failed when outcome.Detail?.StartsWith("Watchdog", StringComparison.Ordinal) == true:
                AddAnomaly(AnomalyCode.StressTimedOut, phase, outcome.Detail);
                break;
            case StressEnd.Failed:
                AddAnomaly(AnomalyCode.StressAbnormalExit, phase, outcome.Detail);
                break;
            case StressEnd.Unsupported:
                AddAnomaly(AnomalyCode.GpuUnsupported, phase, outcome.Detail);
                break;
            case StressEnd.Cancelled when cancellationToken.IsCancellationRequested:
                AddAnomaly(AnomalyCode.UserStopped, phase, null);
                break;
        }
    }

    private void AddAnomaly(AnomalyCode code, TestPhase phase, string? detail)
    {
        lock (_gate)
        {
            _anomalies.Add(new Anomaly(code, phase, DateTimeOffset.Now, detail));
        }
    }

    private void AddSkipped(TestPhase phase, TestStatus status, bool isRequested, double planned) =>
        _phases.Add(new PhaseResult(phase, status, isRequested, null, null, planned, 0, null, null, null, null, null, 0, 0));

    private void AddStopped(TestPhase phase, double planned) =>
        AddSkipped(phase, TestStatus.Cancelled, isRequested: true, planned);

    private static async Task DelayUntilCancelled(TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping early is a normal path: the caller records it.
        }
    }

    private static List<SensorReading> PhaseSamples(MonitoringSession session, TestPhase phase) =>
        session.Samples().Where(sample => sample.Phase == phase).Select(sample => sample.Reading).ToList();

    private static bool HasSufficient(List<SensorReading> samples, TestPlan plan, double seconds, Func<SensorReading, bool> selector)
    {
        var expected = Math.Max(1, Math.Floor(seconds / plan.Profile.SampleIntervalSeconds));
        return samples.Where(selector).GroupBy(reading => reading.SensorId).Any(group => group.Count() >= expected * SensorStatisticsCalculator.MIN_VALID_SAMPLE_RATIO);
    }

    private static (double? Average, double? Maximum) SensorStats(List<SensorReading> samples, Func<SensorReading, bool> selector)
    {
        var groups = samples.Where(selector).GroupBy(reading => reading.SensorId).ToList();
        if (groups.Count == 0)
        {
            return (null, null);
        }

        // With identical cards or several load sensors, the busiest sensor shows whether the load really ran.
        var busiest = groups.MaxBy(group => group.Average(reading => reading.Value!.Value))!;
        return (busiest.Average(reading => reading.Value!.Value), busiest.Max(reading => reading.Value!.Value));
    }

    private static bool IsValid(SensorReading reading, string sensorType) =>
        reading.SensorType == sensorType && reading is { Status: SensorStatus.Ok, Value: not null };

    private static bool CpuTemperatureSelector(SensorReading reading) =>
        SensorSelector.IsCpu(reading.HardwareType) && IsValid(reading, "Temperature") && reading.SensorName == SensorSelector.CpuTemperatureName([reading]);

    private static bool CpuLoadSelector(SensorReading reading) =>
        SensorSelector.IsCpu(reading.HardwareType) && IsValid(reading, "Load") && reading.SensorName == "CPU Total";

    private static Func<SensorReading, bool> GpuTemperatureSelector(TestPlan plan) => reading =>
        SensorSelector.MatchesGpu(reading.HardwareType, reading.HardwareName, plan.TargetGpu?.Model) && IsValid(reading, "Temperature") && reading.SensorName == "GPU Core";

    private static Func<SensorReading, bool> GpuLoadSelector(TestPlan plan) => reading =>
        SensorSelector.MatchesGpu(reading.HardwareType, reading.HardwareName, plan.TargetGpu?.Model) && IsValid(reading, "Load") && SensorSelector.IsGpuLoadName(reading.SensorName);

    private sealed record StressContext(
        TestPhase Phase,
        double PlannedSeconds,
        string? TargetDevice,
        double MinLoadPercent,
        Func<SensorReading, bool> IsTemperatureValid,
        Func<SensorReading, bool> IsLoadValid,
        double ProtectionCelsius,
        IStressLoad Load);

    /// <summary>Checks one stress phase against its limits on every sample and cancels the load when a limit is crossed.</summary>
    private sealed class ProtectionWatch(StressContext stress, CancellationTokenSource stop, bool isMonitored, TimeSpan sensorLostAfter)
    {
        // A monitored device had a valid reading just before the load started, so silence counts from now.
        private DateTimeOffset? _lastValidAt = isMonitored ? DateTimeOffset.Now : null;

        public bool WasOverheated { get; private set; }

        public bool WasSensorLost { get; private set; }

        public double? TriggerValue { get; private set; }

        public bool WasTriggered => WasOverheated || WasSensorLost;

        public void Check(IReadOnlyList<SensorReading> readings)
        {
            if (WasTriggered)
            {
                return;
            }

            var hottest = readings.Where(stress.IsTemperatureValid).Max(reading => reading.Value);
            var now = DateTimeOffset.Now;
            if (hottest is { } value)
            {
                _lastValidAt = now;
                if (value >= stress.ProtectionCelsius)
                {
                    WasOverheated = true;
                    TriggerValue = value;
                    stop.Cancel();
                }

                return;
            }

            // A confirmed unmonitored run has no reading to lose.
            if (_lastValidAt is { } last && now - last >= sensorLostAfter)
            {
                WasSensorLost = true;
                stop.Cancel();
            }
        }
    }

    /// <summary>Links the sampler thread to the running phase: it updates progress and applies protection.</summary>
    private sealed class PhaseMonitor(TestController owner, MonitoringSession session, TestPlan plan) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly Stopwatch _clock = new();
        private double _plannedSeconds;
        private IReadOnlyList<ProtectionWatch> _protections = [];

        public void Begin(TestPhase phase, double plannedSeconds, IReadOnlyList<ProtectionWatch>? protections = null)
        {
            lock (_gate)
            {
                session.BeginPhase(phase);
                _plannedSeconds = plannedSeconds;
                _protections = protections ?? [];
                _clock.Restart();
            }
        }

        public void EndStress()
        {
            lock (_gate)
            {
                _protections = [];
            }
        }

        public void OnSampled(IReadOnlyList<SensorReading> readings)
        {
            IReadOnlyList<ProtectionWatch> protections;
            double elapsed;
            double planned;
            lock (_gate)
            {
                protections = _protections;
                elapsed = _clock.Elapsed.TotalSeconds;
                planned = _plannedSeconds;
            }

            foreach (var protection in protections)
            {
                protection.Check(readings);
            }

            owner.ProgressChanged?.Invoke(new TestProgress(
                session.CurrentPhase,
                elapsed,
                planned,
                SensorSelector.CpuTemperature(readings),
                SensorSelector.GpuTemperature(readings, plan.TargetGpu?.Model),
                SensorSelector.CpuLoad(readings),
                SensorSelector.GpuLoad(readings, plan.TargetGpu?.Model),
                readings));
        }

        public void Dispose()
        {
            EndStress();
        }
    }
}
