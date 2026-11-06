namespace AkikazuHardwareCheck.App;

/// <summary>Cross-process signal used by a second launch to restore the one running main window.</summary>
internal static class SingleInstanceListener
{
    public const string RestoreEventName = "Local\\AkikazuHardwareCheck.Restore";

    public static bool NotifyExisting()
    {
        try
        {
            using var restore = EventWaitHandle.OpenExisting(RestoreEventName);
            restore.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static EventWaitHandle CreateRestoreEvent() =>
        new(false, EventResetMode.AutoReset, RestoreEventName);
}
