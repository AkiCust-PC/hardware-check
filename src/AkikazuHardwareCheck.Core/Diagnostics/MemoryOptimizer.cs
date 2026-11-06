using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AkikazuHardwareCheck.Core.Diagnostics;

public sealed record MemoryCleanupResult(int Attempted, int Succeeded, int Skipped, ulong AvailableBeforeBytes, ulong AvailableAfterBytes);

/// <summary>
/// Trims the working sets of other user processes. This is deliberately explicit: it can page active programs back in
/// and cause short stutter, so it must never run automatically or be described as a performance improvement.
/// </summary>
public static class MemoryOptimizer
{
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const long MINIMUM_WORKING_SET_BYTES = 16L * 1024 * 1024;

    // These processes own the session, the desktop or security tokens; trimming them can destabilise Windows.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "fontdrvhost", "dwm", "LogonUI", "WUDFHost",
    };

    public static MemoryCleanupResult CleanOtherProcesses()
    {
        var before = AvailablePhysicalBytes();
        var attempted = 0;
        var succeeded = 0;
        var skipped = 0;
        var currentSession = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (ShouldSkip(process, currentSession))
                {
                    skipped++;
                    continue;
                }

                attempted++;
                var handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, process.Id);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (EmptyWorkingSet(handle))
                    {
                        succeeded++;
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }

        return new MemoryCleanupResult(attempted, succeeded, skipped, before, AvailablePhysicalBytes());
    }

    public static bool IsProtectedName(string name) => ProtectedNames.Contains(name);

    private static bool ShouldSkip(Process process, int currentSession)
    {
        try
        {
            return process.Id <= 4
                || process.Id == Environment.ProcessId
                || process.SessionId != currentSession
                || IsProtectedName(process.ProcessName)
                || process.WorkingSet64 < MINIMUM_WORKING_SET_BYTES;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return true;
        }
    }

    private static ulong AvailablePhysicalBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.AvailPhysical : 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailPhysical;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
