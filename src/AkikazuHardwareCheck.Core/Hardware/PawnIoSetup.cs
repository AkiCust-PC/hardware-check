using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace AkikazuHardwareCheck.Core.Hardware;

public enum DriverSetupResult
{
    AlreadyInstalled,
    Installed,
    RebootRequired,
    NeedsAdministrator,
    Failed,
}

public sealed record DriverSetupOutcome(DriverSetupResult Result, int? ExitCode);

/// <summary>
/// Installs the sensor driver from the official signed PawnIO installer built into this program, which its author allows
/// to be redistributed unmodified. Staff never see a separate step, and a driver this run installed is removed on exit,
/// so a customer's PC is left as it was found.
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

        var exitCode = RunInstaller(INSTALL_ARGUMENTS, InstallTimeout);
        return new DriverSetupOutcome(exitCode is { } code ? MapExitCode(code) : DriverSetupResult.Failed, exitCode);
    }

    /// <summary>Returns whether the driver was removed.</summary>
    public static bool Uninstall() => RunInstaller(UNINSTALL_ARGUMENTS, UninstallTimeout) is EXIT_SUCCESS or EXIT_REBOOT_REQUIRED;

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
