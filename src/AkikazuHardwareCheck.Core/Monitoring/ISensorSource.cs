using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Monitoring;

/// <summary>Every sensor backend goes through this, so sampling, statistics and tests do not depend on one library.</summary>
public interface ISensorSource : IDisposable
{
    IReadOnlyList<SensorReading> Read();
}
