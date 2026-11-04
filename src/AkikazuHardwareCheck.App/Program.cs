using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using AkikazuHardwareCheck.App;

const string NO_ELEVATE_ARGUMENT = "--no-elevate";

// CPU temperatures need administrator rights, so the tool asks for them right away and staff answer one Windows prompt.
// Declining is not an error: this copy then keeps running with what ordinary rights can read.
if (!args.Contains(NO_ELEVATE_ARGUMENT) && !IsAdministrator() && Environment.ProcessPath is { } path)
{
    try
    {
        Process.Start(new ProcessStartInfo(path, NO_ELEVATE_ARGUMENT) { UseShellExecute = true, Verb = "runas" })?.Dispose();
        return;
    }
    catch (Win32Exception)
    {
        // Declined, or elevation is not available on this account.
    }
}

ApplicationConfiguration.Initialize();

// Lets standard controls and the title bar follow the Windows light or dark setting.
Application.SetColorMode(SystemColorMode.System);
Application.Run(new MainForm());

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}