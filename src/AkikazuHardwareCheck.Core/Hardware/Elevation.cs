using System.Security.Principal;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>One answer to "is this process elevated", used by startup, hardware reads and cleanup.</summary>
public static class Elevation
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
