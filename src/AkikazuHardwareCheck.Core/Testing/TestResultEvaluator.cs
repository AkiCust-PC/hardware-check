using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Testing;

public static class TestResultEvaluator
{
    /// <summary>"No obvious issue" needs every requested phase to pass. Anything unmeasured makes the run incomplete.</summary>
    public static OverallResult Evaluate(IEnumerable<PhaseResult> phases)
    {
        var requested = phases.Where(phase => phase.IsRequested).ToList();
        if (requested.Any(phase => phase.Status == TestStatus.IssueFound))
        {
            return OverallResult.NeedsReview;
        }

        return requested.Count > 0 && requested.All(phase => phase.Status == TestStatus.Passed)
            ? OverallResult.NoObviousIssue
            : OverallResult.Incomplete;
    }

    /// <summary>
    /// A hardware finding outranks missing data. A failed test program is not a hardware finding, and an explicit
    /// cancellation is not an incomplete measurement: both are kept separate so the report never calls either one a pass.
    /// </summary>
    public static TestStatus EvaluateStress(StressEnd end, bool hasTemperature, bool isLoadKnown, bool isLoadSufficient, bool wasOverheated)
    {
        if (end is StressEnd.VerificationFailed or StressEnd.DeviceLost or StressEnd.DriverReset || wasOverheated)
        {
            return TestStatus.IssueFound;
        }

        return end switch
        {
            StressEnd.Unsupported => TestStatus.Unsupported,
            StressEnd.Cancelled => TestStatus.Cancelled,
            StressEnd.Failed => TestStatus.ExecutionFailed,
            _ when isLoadKnown && !isLoadSufficient => TestStatus.IssueFound,
            _ when !hasTemperature || !isLoadKnown => TestStatus.InsufficientData,
            _ => TestStatus.Passed,
        };
    }
}
