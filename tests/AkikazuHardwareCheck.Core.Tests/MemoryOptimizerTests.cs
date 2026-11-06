using AkikazuHardwareCheck.Core.Diagnostics;

namespace AkikazuHardwareCheck.Core.Tests;

public sealed class MemoryOptimizerTests
{
    [Fact]
    public void ProtectedWindowsProcessesAreNeverTrimmed()
    {
        Assert.True(MemoryOptimizer.IsProtectedName("lsass"));
        Assert.True(MemoryOptimizer.IsProtectedName("SYSTEM"));
        Assert.False(MemoryOptimizer.IsProtectedName("explorer"));
        Assert.False(MemoryOptimizer.IsProtectedName("notepad"));
    }
}
