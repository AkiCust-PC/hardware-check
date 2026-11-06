using System.Drawing.Imaging;
using AkikazuHardwareCheck.App;
using AkikazuHardwareCheck.Core.Configuration;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App.SmokeTests;

/// <summary>
/// Creates every window once in each language without showing it. A missing localization key throws in a constructor,
/// which unit tests over Core cannot see; this is the cheapest check that catches that class of release blocker.
/// <c>--screenshot &lt;folder&gt;</c> additionally shows a few windows and captures them for a visual check.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args is ["--screenshot", var folder])
        {
            return CaptureWindows(folder);
        }

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

    private static int CaptureWindows(string folder)
    {
        Directory.CreateDirectory(folder);
        var t = Translator.Load(AppLanguage.SIMPLIFIED_CHINESE);
        using var font = Ui.FontFor(AppLanguage.SIMPLIFIED_CHINESE);

        using (var main = new MainForm())
        {
            main.StartPosition = FormStartPosition.Manual;
            main.Location = Point.Empty;
            main.TopMost = true;
            main.Show();
            main.BringToFront();
            Pump(3000);
            Console.WriteLine($"main size={main.Size} client={main.ClientSize} dpi={main.DeviceDpi} screen={Screen.PrimaryScreen?.Bounds}");
            foreach (var name in new[] { "_rootPanel", "_bodyPanel", "_rightPanel", "_monitorPanel", "_hardwareGroup" })
            {
                var field = typeof(MainForm).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(main) is Control control)
                {
                    Console.WriteLine($"{name}: {control.Bounds} visible={control.Visible} dock={control.Dock}");
                }
            }

            var monitorField = typeof(MainForm).GetField("_monitorPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (monitorField?.GetValue(main) is TableLayoutPanel monitor)
            {
                Console.WriteLine($"monitor columns={string.Join(",", monitor.ColumnStyles.Cast<ColumnStyle>().Select(style => $"{style.SizeType}:{style.Width}"))}");
                foreach (Control child in monitor.Controls)
                {
                    Console.WriteLine($"  card {child.GetType().Name} {child.Bounds} span={monitor.GetColumnSpan(child)}");
                }
            }

            Capture(main, Path.Combine(folder, "main.png"));
        }

        using (var settings = new SettingsForm(t, new AppSettings(), font))
        {
            settings.TopMost = true;
            settings.Show();
            Find<TabControl>(settings)?.SelectTab(2);
            Pump(800);
            Capture(settings, Path.Combine(folder, "settings-crosshair.png"));
        }

        var snapshot = new LiveSnapshot(
            DateTimeOffset.Now, 18, 55, 42, 66, 47, 8, 1200, 8400, "Test",
            4300, 65, 2100, 140, 6L << 30, 12L << 30, 9);
        using (var hud = new GameHudForm(t, () => snapshot, font, 12, Color.White, 0x3FFF))
        {
            hud.Show();
            Pump(500);
            Capture(hud, Path.Combine(folder, "performance-overlay.png"));
        }

        using (var floating = new FloatingPerformanceForm(t, () => snapshot, font))
        {
            floating.Show();
            Pump(800);
            Capture(floating, Path.Combine(folder, "floating.png"));
        }

        using (var junk = new JunkCleanupForm(t, font))
        {
            junk.Show();
            Pump(6000);
            Capture(junk, Path.Combine(folder, "junk-cleanup.png"));
        }

        Console.WriteLine($"Screenshots written to {Path.GetFullPath(folder)}");
        return 0;
    }

    private static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static void Capture(Form form, string path)
    {
        var bounds = form.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static T? Find<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                return match;
            }

            if (Find<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
