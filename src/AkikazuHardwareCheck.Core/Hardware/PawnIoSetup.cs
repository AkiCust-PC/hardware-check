using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.Core.Hardware;

public enum DriverSetupResult
{
    AlreadyInstalled,
    Installed,
    RebootRequired,
    NeedsAdministrator,
    Failed,
}

/// <param name="Result">How the install went.</param>
/// <param name="ExitCode">The installer's exit code when it ran.</param>
/// <param name="HadDriverBefore">Whether the driver service already existed, which may belong to other software.</param>
public sealed record DriverSetupOutcome(DriverSetupResult Result, int? ExitCode, bool HadDriverBefore = false);

/// <summary>
/// Installs the sensor driver from the official signed PawnIO installer built into this program, which its author allows
/// to be redistributed unmodified. Staff never see a separate step, and a driver this run installed is removed on exit,
/// so a customer's PC is left as it was found. The official uninstaller leaves the driver service and its driver package
/// behind, so those are removed here too, but only when this run is what put them there.
/// </summary>
public static class PawnIoSetup
{
    /// <summary>The installer's hash at the pinned version, checked again before every run in case the temp copy was changed.</summary>
    public const string INSTALLER_SHA256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    private const string RESOURCE_NAME = "pawnio.setup.exe";
    private const string INSTALL_ARGUMENTS = "-install -silent";
    private const string UNINSTALL_ARGUMENTS = "-uninstall -silent";
    private const int EXIT_SUCCESS = 0;

    // ERROR_SUCCESS_REBOOT_REQUIRED, which PawnIO 2.2.0 returns in silent mode when a restart is needed.
    private const int EXIT_REBOOT_REQUIRED = 3010;
    private const string SERVICE_KEY = @"SYSTEM\CurrentControlSet\Services\PawnIO";
    private const string INF_SEARCH_PATTERN = "oem*.inf";
    private const string INF_DRIVER_FILE = "PawnIO.sys";
    private const string INF_PROVIDER = "namazso";

    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UninstallTimeout = TimeSpan.FromSeconds(30);

    public static DriverSetupResult MapExitCode(int exitCode) => exitCode switch
    {
        EXIT_SUCCESS => DriverSetupResult.Installed,
        EXIT_REBOOT_REQUIRED => DriverSetupResult.RebootRequired,
        _ => DriverSetupResult.Failed,
    };

    public static bool MatchesPinnedInstaller(ReadOnlySpan<byte> installer) =>
        Convert.ToHexString(SHA256.HashData(installer)) == INSTALLER_SHA256;

    /// <summary>Failures come back as a result: the rest of the tool works without this driver.</summary>
    public static DriverSetupOutcome Install(bool isAdministrator, bool isInstalled)
    {
        if (isInstalled)
        {
            return new DriverSetupOutcome(DriverSetupResult.AlreadyInstalled, null);
        }

        if (!isAdministrator)
        {
            return new DriverSetupOutcome(DriverSetupResult.NeedsAdministrator, null);
        }

        var hadDriver = IsDriverServicePresent();
        var exitCode = RunInstaller(INSTALL_ARGUMENTS, InstallTimeout);
        return new DriverSetupOutcome(exitCode is { } code ? MapExitCode(code) : DriverSetupResult.Failed, exitCode, hadDriver);
    }

    /// <summary>Returns whether everything to be removed is gone.</summary>
    /// <param name="removeDriverPackage">True when this run created the driver service, so nothing else can depend on it.</param>
    public static bool Uninstall(bool removeDriverPackage)
    {
        var isUninstalled = RunInstaller(UNINSTALL_ARGUMENTS, UninstallTimeout) is EXIT_SUCCESS or EXIT_REBOOT_REQUIRED;
        return isUninstalled && (!removeDriverPackage || RemoveDriverPackage());
    }

    /// <summary>Both markers must match, so another vendor's driver package is never selected.</summary>
    public static bool IsPawnIoInf(string text) =>
        text.Contains(INF_DRIVER_FILE, StringComparison.OrdinalIgnoreCase) && text.Contains(INF_PROVIDER, StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows renames an installed INF to oemN.inf, and pnputil's listing changes with the system language, so the files themselves are read.</summary>
    public static List<string> FindDriverInfs(string folder)
    {
        var names = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder, INF_SEARCH_PATTERN))
        {
            try
            {
                if (IsPawnIoInf(File.ReadAllText(path)))
                {
                    names.Add(Path.GetFileName(path));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable INF cannot be confirmed as this driver's, so it is left alone.
                Trace.TraceWarning(exception.Message);
            }
        }

        return names;
    }

    private static bool IsDriverServicePresent()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SERVICE_KEY);
        return key is not null;
    }

    private static bool RemoveDriverPackage()
    {
        var infFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        foreach (var inf in FindDriverInfs(infFolder))
        {
            RunSystemTool("pnputil.exe", $"/delete-driver {inf} /uninstall /force");
        }

        if (IsDriverServicePresent())
        {
            RunSystemTool("sc.exe", "stop PawnIO");
            RunSystemTool("sc.exe", "delete PawnIO");
        }

        return !IsDriverServicePresent() && FindDriverInfs(infFolder).Count == 0;
    }

    /// <summary>The result is judged by checking what is left afterwards, not by these exit codes.</summary>
    private static void RunSystemTool(string name, string arguments)
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, name);
            using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false, CreateNoWindow = true });
            if (process is not null && !process.WaitForExit(UninstallTimeout))
            {
                process.Kill();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.TraceWarning(exception.Message);
        }
    }

    private static int? RunInstaller(string arguments, TimeSpan timeout)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"AkikazuHardwareCheck-{Guid.NewGuid():N}");
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE_NAME);
            if (resource is null)
            {
                return null;
            }

            using var copy = new MemoryStream();
            resource.CopyTo(copy);
            if (!MatchesPinnedInstaller(copy.GetBuffer().AsSpan(0, (int)copy.Length)))
            {
                return null;
            }

            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "PawnIO_setup.exe");
            File.WriteAllBytes(path, copy.ToArray());
            using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder });
            if (process is null)
            {
                return null;
            }

            if (process.WaitForExit(timeout))
            {
                return process.ExitCode;
            }

            process.Kill();
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            TryDelete(folder);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows may still hold the finished installer for a moment. A leftover temp file does no harm.
            Trace.TraceWarning(exception.Message);
        }
    }
}
