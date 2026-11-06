using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

internal static class Program
{
    public const string REMOVE_DRIVER_ARGUMENT = "--remove-driver";

    private const string NO_ELEVATE_ARGUMENT = "--no-elevate";
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(30);

    [STAThread]
    private static void Main(string[] args)
    {
        if (args is [REMOVE_DRIVER_ARGUMENT, var parentId, var removesPackage, var language])
        {
            RemoveDriverAfterExit(parentId, removesPackage, language);
            return;
        }

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
        InstallCrashGuard();

        // Lets standard controls and the title bar follow the Windows light or dark setting.
        Application.SetColorMode(SystemColorMode.System);
        try
        {
            Application.Run(new MainForm(args.Contains("--tray", StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception exception)
        {
            ExitSilently(exception);
        }
    }

    /// <summary>
    /// A hardware diagnostic tool must not put an exception dialog in front of the operator. UI, worker-thread and
    /// unobserved-task failures are written locally and terminate the process without a dialog.
    /// </summary>
    private static void InstallCrashGuard()
    {
        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ExitSilently(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ExitSilently(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            ExitSilently(e.Exception);
        };
    }

    private static void ExitSilently(Exception? exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AkikazuHardwareCheck");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                new System.Text.UTF8Encoding(false));
        }
        catch
        {
            // Logging must never turn a silent exit into another dialog.
        }

        Environment.Exit(1);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    /// <summary>The driver can only stop once the main program has closed it, which is when that process ends.</summary>
    private static void RemoveDriverAfterExit(string parentId, string removesPackage, string language)
    {
        WaitForExit(parentId);
        if (PawnIoSetup.Uninstall(bool.Parse(removesPackage)))
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        var t = Translator.Load(language);
        MessageBox.Show(t["ui.driverRemoveFailed"], t["ui.appName"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void WaitForExit(string processId)
    {
        try
        {
            using var parent = Process.GetProcessById(int.Parse(processId, CultureInfo.InvariantCulture));
            parent.WaitForExit(ParentExitTimeout);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}