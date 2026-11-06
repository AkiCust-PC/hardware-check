using AkikazuHardwareCheck.App;
using AkikazuHardwareCheck.Core.Configuration;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App.SmokeTests;

/// <summary>
/// Creates every window once in each language without showing it. A missing localization key throws in a constructor,
/// which unit tests over Core cannot see; this is the cheapest check that catches that class of release blocker.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var failures = new List<string>();

        foreach (var language in AppLanguage.Supported)
        {
            try
            {
                var t = Translator.Load(language);
                using var font = Ui.FontFor(language);
                using var camera = new CameraTestForm(t, font);
                using var settings = new SettingsForm(t, new AppSettings(), font);
                using var network = new NetworkSpeedTestForm(t, font);
                using var display = new DisplayInfoForm(t, font);
                using var about = new AboutForm(t, font);
                using var maintenance = new MaintenanceForm(t, font);
                using var disk = new DiskTestForm(t, font);
                using var junk = new JunkCleanupForm(t, font);
            }
            catch (Exception exception)
            {
                failures.Add($"{language}: {exception}");
            }
        }

        try
        {
            using var main = new MainForm();
            _ = main.Handle;
        }
        catch (Exception exception)
        {
            failures.Add($"MainForm: {exception}");
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("Smoke test passed: every window was created in every language.");
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        return 1;
    }
}
