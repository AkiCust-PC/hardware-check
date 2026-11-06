using Microsoft.Win32;

namespace AkikazuHardwareCheck.App;

/// <summary>Current-user auto-start. It never needs administrator rights and never touches machine-wide entries.</summary>
internal static class StartupRegistration
{
    private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string VALUE_NAME = "AkikazuHardwareCheck";
    private const string TRAY_ARGUMENT = "--tray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, writable: false);
        return key?.GetValue(VALUE_NAME) is string value && value.Contains(TRAY_ARGUMENT, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RUN_KEY, writable: true);
        if (!enabled)
        {
            key.DeleteValue(VALUE_NAME, throwOnMissingValue: false);
            return;
        }

        if (Environment.ProcessPath is not { } path)
        {
            return;
        }

        key.SetValue(VALUE_NAME, $"\"{path}\" {TRAY_ARGUMENT}", RegistryValueKind.String);
    }
}
