using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Hardware;

namespace AkikazuHardwareCheck.Core.Diagnostics;

public enum JunkCategory
{
    UserTemp,
    WindowsTemp,
    RecycleBin,
    ThumbnailCache,
}

public sealed record JunkEntry(JunkCategory Category, long Bytes, int Files, bool RequiresAdministrator);

public sealed record JunkScan(IReadOnlyList<JunkEntry> Entries, bool IsAdministrator);

public sealed record JunkCleanResult(int DeletedFiles, long DeletedBytes, int SkippedFiles, bool Completed);

/// <summary>
/// Only known disposable locations are touched. There is no registry cleaning, no prefetch deletion and no
/// Windows Update cache deletion; files in use are skipped instead of forced.
/// </summary>
public static class JunkCleaner
{
    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    public static JunkScan Scan()
    {
        var entries = new List<JunkEntry>();
        var windowsTemp = WindowsTempPath();
        entries.Add(Measure(JunkCategory.UserTemp, Path.GetTempPath(), requiresAdministrator: false));
        if (windowsTemp is not null)
        {
            entries.Add(Measure(JunkCategory.WindowsTemp, windowsTemp, requiresAdministrator: true));
        }

        entries.Add(Measure(JunkCategory.ThumbnailCache, ThumbnailPath(), requiresAdministrator: false));
        if (TryQueryRecycleBin(out var recycleBytes, out var recycleItems))
        {
            entries.Add(new JunkEntry(JunkCategory.RecycleBin, recycleBytes, (int)Math.Min(recycleItems, int.MaxValue), RequiresAdministrator: false));
        }

        return new JunkScan(entries, Elevation.IsAdministrator());
    }

    public static JunkCleanResult Clean(JunkCategory category)
    {
        if (category == JunkCategory.RecycleBin)
        {
            var result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return new JunkCleanResult(0, 0, 0, result == 0);
        }

        var path = category switch
        {
            JunkCategory.UserTemp => Path.GetTempPath(),
            JunkCategory.WindowsTemp => WindowsTempPath(),
            JunkCategory.ThumbnailCache => ThumbnailPath(),
            _ => null,
        };
        if (path is null || !Directory.Exists(path))
        {
            return new JunkCleanResult(0, 0, 0, true);
        }

        var files = 0;
        var bytes = 0L;
        var skipped = 0;
        var completed = true;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                try
                {
                    if (Directory.Exists(entry))
                    {
                        if (category == JunkCategory.ThumbnailCache || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        var before = DirectorySize(entry);
                        var count = CountFiles(entry);
                        Directory.Delete(entry, recursive: true);
                        files += count;
                        bytes += before;
                    }
                    else if (category != JunkCategory.ThumbnailCache || Path.GetFileName(entry).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase))
                    {
                        var size = new FileInfo(entry).Length;
                        File.Delete(entry);
                        files++;
                        bytes += size;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The directory itself may be protected; the caller still gets the partial counts, but not "completed".
            completed = false;
        }

        return new JunkCleanResult(files, bytes, skipped, completed);
    }

    private static JunkEntry Measure(JunkCategory category, string? path, bool requiresAdministrator)
    {
        if (path is null || !Directory.Exists(path))
        {
            return new JunkEntry(category, 0, 0, requiresAdministrator);
        }

        var bytes = 0L;
        var files = 0;
        try
        {
            foreach (var file in AllFiles(path))
            {
                if (category == JunkCategory.ThumbnailCache && !Path.GetFileName(file).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    bytes += new FileInfo(file).Length;
                    files++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A locked file is not counted.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A protected directory still appears with the count collected so far.
        }

        return new JunkEntry(category, bytes, files, requiresAdministrator);
    }

    /// <summary>Junctions and symlinks in a temp folder must not make the scan follow another volume or loop.</summary>
    private static IEnumerable<string> AllFiles(string path) => Directory.EnumerateFiles(
        path,
        "*",
        new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint });

    private static long DirectorySize(string path)
    {
        try
        {
            return AllFiles(path).Sum(file =>
            {
                try
                {
                    return new FileInfo(file).Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return 0;
                }
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static int CountFiles(string path)
    {
        try
        {
            return AllFiles(path).Count();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string? WindowsTempPath()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return string.IsNullOrWhiteSpace(windows) ? null : Path.Combine(windows, "Temp");
    }

    private static string ThumbnailPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");

    private static bool TryQueryRecycleBin(out long bytes, out long items)
    {
        var info = new RecycleBinInfo { Size = (uint)Marshal.SizeOf<RecycleBinInfo>() };
        var result = SHQueryRecycleBin(null, ref info);
        bytes = result == 0 ? info.SizeBytes : 0;
        items = result == 0 ? info.ItemCount : 0;
        return result == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo
    {
        public uint Size;
        public long SizeBytes;
        public long ItemCount;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref RecycleBinInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr window, string? rootPath, uint flags);
}
