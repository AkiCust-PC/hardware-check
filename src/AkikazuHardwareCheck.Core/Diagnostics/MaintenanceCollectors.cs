using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.Core.Diagnostics;

public sealed record StartupEntry(string Name, string Command, string Source);

public sealed record InstalledProgram(
    string Name,
    string? Version,
    string? Publisher,
    string? InstallDate,
    string? UninstallCommand);

public sealed record RunningProcess(string Name, int Id, long WorkingSetBytes, string? Company);

/// <summary>Read-only system lists used by the maintenance window. None of these sources changes the machine.</summary>
public static class MaintenanceCollectors
{
    private const string RUN_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RUN_ONCE_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string UNINSTALL_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<StartupEntry> ReadStartup()
    {
        var entries = new List<StartupEntry>();
        foreach (var (hive, view, label) in RegistryScopes())
        {
            ReadRunKey(hive, view, RUN_KEY, $"{label} Run", entries);
            ReadRunKey(hive, view, RUN_ONCE_KEY, $"{label} RunOnce", entries);
        }

        AddStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup", entries);
        AddStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup", entries);
        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Distinct()
            .OrderBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<InstalledProgram> ReadInstalledPrograms()
    {
        var programs = new List<InstalledProgram>();
        foreach (var (hive, view, _) in RegistryScopes())
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UNINSTALL_KEY);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key is null || ReadInt(key, "SystemComponent") == 1)
                    {
                        continue;
                    }

                    var displayName = ReadString(key, "DisplayName");
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    programs.Add(new InstalledProgram(
                        displayName,
                        ReadString(key, "DisplayVersion"),
                        ReadString(key, "Publisher"),
                        ReadString(key, "InstallDate"),
                        ReadString(key, "UninstallString") ?? ReadString(key, "QuietUninstallString")));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // One unreadable hive must not remove programs found in the others.
            }
        }

        return programs
            .Distinct()
            .OrderBy(program => program.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<RunningProcess> ReadProcesses()
    {
        var processes = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    processes.Add(new RunningProcess(
                        process.ProcessName,
                        process.Id,
                        process.WorkingSet64,
                        process.MainModule?.FileVersionInfo.CompanyName));
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    processes.Add(new RunningProcess(process.ProcessName, process.Id, 0, null));
                }
            }
        }

        return processes
            .OrderByDescending(process => process.WorkingSetBytes)
            .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FormatBytes(long bytes) =>
        (bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    public static string FormatInstallDate(string? value)
    {
        if (value is { Length: 8 } && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value ?? "";
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View, string Label)> RegistryScopes()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM");
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32");
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU");
        yield return (RegistryHive.CurrentUser, RegistryView.Registry32, "HKCU32");
    }

    private static void ReadRunKey(RegistryHive hive, RegistryView view, string path, string source, List<StartupEntry> entries)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string command && !string.IsNullOrWhiteSpace(command))
                {
                    entries.Add(new StartupEntry(name, command, source));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A missing or unreadable key is not an error for the other startup locations.
        }
    }

    private static void AddStartupFolder(string folder, string source, List<StartupEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                entries.Add(new StartupEntry(Path.GetFileNameWithoutExtension(path), path, source));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The other startup folders still stand.
        }
    }

    private static string? ReadString(RegistryKey key, string name) => key.GetValue(name) as string;

    private static int? ReadInt(RegistryKey key, string name) => key.GetValue(name) is int value ? value : null;
}
