using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Testing;

public interface IStressLoad
{
    /// <summary>Runs until the duration ends, the token cancels or the load fails. Failures come back in the outcome.</summary>
    Task<StressOutcome> RunAsync(TimeSpan duration, CancellationToken cancellationToken);
}
