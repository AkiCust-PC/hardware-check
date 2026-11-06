namespace AkikazuHardwareCheck.Core.Hardware;

public sealed record StorageVolume(
    string Root,
    string? Label,
    string FileSystem,
    string DriveType,
    long TotalBytes,
    long FreeBytes);

/// <summary>Logical volumes from DriveInfo. This is the lightweight view; physical partition mapping stays separate.</summary>
public static class StorageVolumeCollector
{
    public static List<StorageVolume> Read()
    {
        var volumes = new List<StorageVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                volumes.Add(new StorageVolume(
                    drive.RootDirectory.FullName,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel,
                    drive.DriveFormat,
                    drive.DriveType.ToString(),
                    drive.TotalSize,
                    drive.AvailableFreeSpace));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A disconnected or protected drive must not hide the other volumes.
            }
        }

        return volumes;
    }
}
