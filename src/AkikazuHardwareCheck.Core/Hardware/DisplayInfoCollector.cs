using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// The display config API supplies active modes and connections; WMI adds EDID details. The EDID serial is never read.
/// </summary>
public static class DisplayInfoCollector
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint DEVICE_INFO_GET_TARGET_NAME = 2;
    private const uint DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const uint MODE_INFO_TYPE_SOURCE = 1;
    private const double CM_PER_INCH = 2.54;
    private const int REFRESH_RATE_DECIMALS = 2;
    private const int DIAGONAL_DECIMALS = 1;

    public static List<DisplayInfo> ReadDisplays(IReadOnlyList<GpuInfo> gpus)
    {
        var (paths, modes) = QueryActivePaths();
        var edidById = TryReadEdid();

        return paths.Select(path =>
        {
            var target = ReadTargetName(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            var key = HardwareText.MonitorKey(target?.MonitorDevicePath);
            var (manufacturer, product) = HardwareText.ParseMonitorIds(key);
            var edid = key is not null ? edidById.GetValueOrDefault(key) : default;
            var source = FindSourceMode(path, modes);
            var luid = GpuInfoCollector.FormatLuid(path.SourceInfo.AdapterId.HighPart, path.SourceInfo.AdapterId.LowPart);
            var advancedColor = ReadAdvancedColor(path.TargetInfo.AdapterId, path.TargetInfo.Id);

            return new DisplayInfo(
                edid.Name ?? HardwareText.Clean(target?.MonitorFriendlyDeviceName),
                manufacturer,
                product,
                edid.Year,
                source is { } size ? (int)size.Width : null,
                source is { } height ? (int)height.Height : null,
                ToRefreshRate(path.TargetInfo.RefreshRate),
                edid.DiagonalInches,
                MapConnection(path.TargetInfo.OutputTechnology),
                source is { PositionX: 0, PositionY: 0 },
                gpus.FirstOrDefault(gpu => gpu.AdapterLuid == luid)?.Model,
                advancedColor?.Supported,
                advancedColor?.Enabled,
                advancedColor?.BitsPerColor,
                advancedColor?.ColorEncoding);
        }).ToList();
    }

    public static double? DiagonalInches(int? widthCm, int? heightCm)
    {
        // EDID uses zero for projectors and other outputs without a fixed physical size.
        if (widthCm is null or <= 0 || heightCm is null or <= 0)
        {
            return null;
        }

        var diagonalCm = Math.Sqrt(((double)widthCm.Value * widthCm.Value) + ((double)heightCm.Value * heightCm.Value));
        return Math.Round(diagonalCm / CM_PER_INCH, DIAGONAL_DECIMALS);
    }

    public static string? MapConnection(uint outputTechnology) => outputTechnology switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        10 => "DisplayPort",
        11 => "Embedded DisplayPort",
        13 => "Embedded UDI",
        15 => "Miracast",
        16 or 17 => "Indirect",
        18 => "USB-C DisplayPort",
        0x80000000 => "Internal",
        _ => null,
    };

    private static double? ToRefreshRate(Rational rate) =>
        rate.Denominator == 0 || rate.Numerator == 0
            ? null
            : Math.Round((double)rate.Numerator / rate.Denominator, REFRESH_RATE_DECIMALS);

    private static SourceMode? FindSourceMode(PathInfo path, ModeInfo[] modes)
    {
        var index = path.SourceInfo.ModeInfoIdx;
        if (index >= modes.Length || modes[index].InfoType != MODE_INFO_TYPE_SOURCE)
        {
            return null;
        }

        return modes[index].Source;
    }

    private static (PathInfo[] Paths, ModeInfo[] Modes) QueryActivePaths()
    {
        // The topology can change between the two calls, which the API reports as an insufficient buffer.
        while (true)
        {
            var result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
            if (result != ERROR_SUCCESS)
            {
                throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed: {result}");
            }

            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == ERROR_INSUFFICIENT_BUFFER)
            {
                continue;
            }

            if (result != ERROR_SUCCESS)
            {
                throw new InvalidOperationException($"QueryDisplayConfig failed: {result}");
            }

            return (paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    private static TargetDeviceName? ReadTargetName(Luid adapterId, uint targetId)
    {
        var request = new TargetDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = DEVICE_INFO_GET_TARGET_NAME,
                Size = (uint)Marshal.SizeOf<TargetDeviceName>(),
                AdapterId = adapterId,
                Id = targetId,
            },
        };

        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request : null;
    }

    /// <summary>Windows 10 1703+ exposes advanced color per target; older systems simply return no data.</summary>
    private static AdvancedColorDetails? ReadAdvancedColor(Luid adapterId, uint targetId)
    {
        var request = new AdvancedColorInfo
        {
            Header = new DeviceInfoHeader
            {
                Type = DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                Size = (uint)Marshal.SizeOf<AdvancedColorInfo>(),
                AdapterId = adapterId,
                Id = targetId,
            },
        };
        if (DisplayConfigGetDeviceInfoAdvancedColor(ref request) != ERROR_SUCCESS)
        {
            return null;
        }

        return new AdvancedColorDetails(
            (request.Value & 1) != 0,
            (request.Value & 2) != 0,
            request.BitsPerColorChannel > 0 ? (int)request.BitsPerColorChannel : null,
            MapColorEncoding(request.ColorEncoding));
    }

    public static string? MapColorEncoding(uint value) => value switch
    {
        0 => "SDR",
        1 => "WCG",
        2 => "HDR10",
        3 => "Dolby Vision",
        4 => "HDR10+",
        _ => null,
    };

    // WMI access to monitor classes can be denied per machine; the display config data still stands without it.
    private static Dictionary<string, EdidDetails> TryReadEdid()
    {
        var details = new Dictionary<string, EdidDetails>();
        try
        {
            var sizes = Wmi.Query(
                Wmi.MONITOR_SCOPE,
                "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams",
                item => (
                    Key: HardwareText.MonitorKey(Wmi.Text(item, "InstanceName")),
                    Diagonal: DiagonalInches(Wmi.Integer(item, "MaxHorizontalImageSize"), Wmi.Integer(item, "MaxVerticalImageSize"))));
            foreach (var (key, diagonal) in sizes)
            {
                if (key is not null)
                {
                    details[key] = new EdidDetails(null, null, diagonal);
                }
            }

            var identities = Wmi.Query(
                Wmi.MONITOR_SCOPE,
                "SELECT InstanceName, UserFriendlyName, YearOfManufacture FROM WmiMonitorID",
                item => (
                    Key: HardwareText.MonitorKey(Wmi.Text(item, "InstanceName")),
                    Name: HardwareText.DecodeCodeUnits(item["UserFriendlyName"] as ushort[]),
                    Year: Wmi.Integer(item, "YearOfManufacture")));
            foreach (var (key, name, year) in identities)
            {
                if (key is not null)
                {
                    details[key] = details.GetValueOrDefault(key) with { Name = name, Year = year };
                }
            }
        }
        catch (Exception exception) when (exception is System.Management.ManagementException or UnauthorizedAccessException)
        {
            return details;
        }

        return details;
    }

    private readonly record struct EdidDetails(string? Name, int? Year, double? DiagonalInches);

    private readonly record struct AdvancedColorDetails(bool Supported, bool Enabled, int? BitsPerColor, string? ColorEncoding);

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public DeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] PathInfo[] paths,
        ref uint modeCount,
        [Out] ModeInfo[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
    private static extern int DisplayConfigGetDeviceInfoAdvancedColor(ref AdvancedColorInfo request);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public int PositionX;
        public int PositionY;
    }

    // Matches DISPLAYCONFIG_MODE_INFO: the 48-byte union starts at offset 16. Target modes are not read.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ModeInfo
    {
        [FieldOffset(0)]
        public uint InfoType;

        [FieldOffset(16)]
        public SourceMode Source;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}
