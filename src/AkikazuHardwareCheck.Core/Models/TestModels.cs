namespace AkikazuHardwareCheck.Core.Models;

public enum TestStatus
{
    Passed,
    IssueFound,
    Incomplete,
    Unsupported,
    NotRun,
    InsufficientData,
}

public enum OverallResult
{
    NoObviousIssue,
    NeedsReview,
    Incomplete,
}

public enum AnomalyCode
{
    CpuVerificationError,
    GpuVerificationError,
    GpuDeviceLost,
    GpuDriverReset,
    StressAbnormalExit,
    StressTimedOut,
    InsufficientSamples,
    LoadBelowTarget,
    LoadUnverified,
    TemperatureProtection,
    SensorLost,
    TemperatureUnavailable,
    IdleConditionNotMet,
    GpuUnsupported,
    UserStopped,
}

public enum StressEnd
{
    Completed,
    Cancelled,
    VerificationFailed,
    DeviceLost,
    DriverReset,
    Failed,
    Unsupported,
}

/// <summary>What a load reports when it finishes. It never throws: every failure is a result to record.</summary>
/// <param name="End">How the load ended.</param>
/// <param name="WorkUnits">Completed work units, showing that work really ran.</param>
/// <param name="VerificationErrors">Computations whose results differed from the known answer.</param>
/// <param name="MeasuredLoadPercent">The load's own measurement, used when no load sensor exists.</param>
/// <param name="Detail">Technical detail for the record.</param>
public sealed record StressOutcome(StressEnd End, long WorkUnits, long VerificationErrors, double? MeasuredLoadPercent, string? Detail);

/// <param name="IdleSeconds">Idle sampling time. The specification's default is 60 seconds.</param>
/// <param name="GpuStressSeconds">GPU load time, 60 to 300 seconds in normal use. A dual test runs for the longer of the two.</param>
/// <param name="CpuStressSeconds">CPU load time, 60 to 300 seconds in normal use.</param>
/// <param name="SampleIntervalSeconds">Time between sensor reads.</param>
/// <param name="CpuProtectionCelsius">The CPU load stops at this temperature.</param>
/// <param name="GpuProtectionCelsius">The GPU load stops at this core temperature.</param>
public sealed record TestProfile(
    double IdleSeconds,
    double GpuStressSeconds,
    double CpuStressSeconds,
    double SampleIntervalSeconds,
    double CpuProtectionCelsius,
    double GpuProtectionCelsius)
{
    private const double MIN_STRESS_SECONDS = 60;
    private const double MAX_STRESS_SECONDS = 300;

    // Many desktop CPUs hold 95 °C or more by design under sustained load, so a lower limit would stop healthy machines.
    public static TestProfile Standard { get; } = new(60, 120, 120, 1, 100, 95);

    public TestProfile ClampedToSupportedRange() => this with
    {
        GpuStressSeconds = Math.Clamp(GpuStressSeconds, MIN_STRESS_SECONDS, MAX_STRESS_SECONDS),
        CpuStressSeconds = Math.Clamp(CpuStressSeconds, MIN_STRESS_SECONDS, MAX_STRESS_SECONDS),
    };
}

public sealed record TestPlan(TestProfile Profile, bool RunIdle, bool RunGpu, bool RunCpu, GpuInfo? TargetGpu)
{
    public static TestPlan Full(TestProfile profile, GpuInfo? targetGpu) => new(profile, true, true, true, targetGpu);
}

public enum TemperatureRowState
{
    Measured,
    InsufficientData,
    Unavailable,
    NotRun,
}

public sealed record TemperatureRow(
    bool IsCpu,
    TestPhase Phase,
    string? SensorName,
    double? Minimum,
    double? Maximum,
    double? Average,
    int ValidSamples,
    TemperatureRowState State);

public enum LoadMetric
{
    CpuPower,
    CpuClock,
    CpuVoltage,
    GpuPower,
}

/// <summary>A value under load. Sensors that could not be read have no row at all.</summary>
public sealed record LoadMetricRow(LoadMetric Metric, double? Average, double? Maximum, string Unit);

public sealed record Anomaly(AnomalyCode Code, TestPhase Phase, DateTimeOffset At, string? Detail);

public sealed record PhaseResult(
    TestPhase Phase,
    TestStatus Status,
    bool IsRequested,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    double PlannedSeconds,
    double ActualSeconds,
    string? TargetDevice,
    double? AverageLoadPercent,
    double? MaximumLoadPercent,
    string? LoadSource,
    string? TemperatureSensor,
    long WorkUnits,
    long VerificationErrors);

public sealed record TestRunResult(
    TestProfile Profile,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    OverallResult Overall,
    IReadOnlyList<PhaseResult> Phases,
    IReadOnlyList<SensorStatistics> Statistics,
    IReadOnlyList<Anomaly> Anomalies,
    string AverageMethod,
    int SensorReads,
    int SensorReadFailures);

/// <summary>A live status update for the interface. Raised on the sampler thread or the test thread.</summary>
public sealed record TestProgress(
    TestPhase Phase,
    double PhaseElapsedSeconds,
    double PhasePlannedSeconds,
    double? CpuTemperature,
    double? GpuTemperature,
    double? CpuLoad,
    double? GpuLoad,
    IReadOnlyList<SensorReading>? Readings = null);
