using System.Text;
using System.Text.Json;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Configuration;

/// <param name="Language">The chosen language. Null follows the system language.</param>
/// <param name="ReportFolder">Where reports go. Null uses the default folder.</param>
/// <param name="GpuStressSeconds">GPU load time.</param>
/// <param name="CpuStressSeconds">CPU load time.</param>
/// <param name="CpuProtectionCelsius">CPU temperature limit.</param>
/// <param name="GpuProtectionCelsius">GPU temperature limit.</param>
/// <param name="IsModelsOnlyView">Whether the hardware list shows only hardware models and parameters.</param>
/// <param name="BackgroundColorArgb">The chosen window color. Null uses the system window color.</param>
/// <param name="BackgroundImagePath">The chosen background picture. Null shows no picture.</param>
/// <param name="BackgroundRotationDegrees">Clockwise rotation of the picture.</param>
/// <param name="IsBackgroundMirrored">Whether the picture is mirrored left to right after rotating.</param>
/// <param name="BackgroundZoom">Picture size relative to just covering the window.</param>
/// <param name="BackgroundPanX">Picture shift as a share of the window width.</param>
/// <param name="BackgroundPanY">Picture shift as a share of the window height.</param>
/// <param name="IsAlwaysOnTop">Whether the window stays above others.</param>
/// <param name="ShowTrayIcon">Whether the notification-area icon is shown.</param>
/// <param name="CloseToTray">Whether closing the main window hides it instead of exiting.</param>
/// <param name="StartWithWindows">Whether the current user starts the program at sign-in.</param>
/// <param name="WindowX">Last main-window left edge.</param>
/// <param name="WindowY">Last main-window top edge.</param>
/// <param name="WindowWidth">Last main-window width.</param>
/// <param name="WindowHeight">Last main-window height.</param>
/// <param name="IsMaximized">Whether the main window was maximized.</param>
/// <param name="FloatingEnabled">Whether the floating window is shown on startup.</param>
/// <param name="FloatingLeft">Last floating-window left edge.</param>
/// <param name="FloatingTop">Last floating-window top edge.</param>
/// <param name="FloatingSnapped">Whether the floating window was docked to an edge.</param>
/// <param name="FloatingEdge">Docked edge: 0 none, 1 left, 2 right, 3 top, 4 bottom.</param>
/// <param name="FloatingInfoVisible">Whether the floating window showed its readings.</param>
public sealed record AppSettings(
    string? Language = null,
    string? ReportFolder = null,
    double? GpuStressSeconds = null,
    double? CpuStressSeconds = null,
    double? CpuProtectionCelsius = null,
    double? GpuProtectionCelsius = null,
    bool IsModelsOnlyView = false,
    int? BackgroundColorArgb = null,
    string? BackgroundImagePath = null,
    int BackgroundRotationDegrees = 0,
    bool IsBackgroundMirrored = false,
    double BackgroundZoom = 1,
    double BackgroundPanX = 0,
    double BackgroundPanY = 0,
    bool IsAlwaysOnTop = false,
    bool ShowTrayIcon = true,
    bool CloseToTray = true,
    bool StartWithWindows = false,
    int? WindowX = null,
    int? WindowY = null,
    int? WindowWidth = null,
    int? WindowHeight = null,
    bool IsMaximized = false,
    bool FloatingEnabled = true,
    int? FloatingLeft = null,
    int? FloatingTop = null,
    bool FloatingSnapped = false,
    int FloatingEdge = 0,
    bool FloatingInfoVisible = true,
    bool HudEnabled = false,
    int HudFontSize = 12,
    int HudColorArgb = unchecked((int)0xFFFFFFFF),
    int HudItems = (1 << 5) - 1 | (1 << 10) | (1 << 13),
    bool RulerEnabled = false,
    int RulerColorArgb = unchecked((int)0xDCFF5050),
    int RulerOpacity = 85,
    int RulerSpacing = 50,
    bool CrosshairEnabled = false,
    int CrosshairShape = 1,
    int CrosshairColorArgb = unchecked((int)0xFFFF0000),
    int CrosshairLength = 16,
    int CrosshairThickness = 2,
    int CrosshairGap = 4,
    int CrosshairOpacity = 100,
    bool IsHardwarePanelHidden = false,
    bool IsMonitorPanelHidden = false)
{
    private const string FOLDER_NAME = "AkikazuHardwareCheck";

    public static string DefaultReportFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), FOLDER_NAME, "Reports");

    public static string DefaultSettingsPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FOLDER_NAME, "settings.json");

    public string EffectiveReportFolder => string.IsNullOrWhiteSpace(ReportFolder) ? DefaultReportFolder : ReportFolder;

    public TestProfile ToProfile() => (TestProfile.Standard with
    {
        GpuStressSeconds = GpuStressSeconds ?? TestProfile.Standard.GpuStressSeconds,
        CpuStressSeconds = CpuStressSeconds ?? TestProfile.Standard.CpuStressSeconds,
        CpuProtectionCelsius = CpuProtectionCelsius ?? TestProfile.Standard.CpuProtectionCelsius,
        GpuProtectionCelsius = GpuProtectionCelsius ?? TestProfile.Standard.GpuProtectionCelsius,
    }).ClampedToSupportedRange();

    /// <summary>A missing or damaged file gives defaults: settings must never stop the tool from starting.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(AppSettings.FloatingEnabled), out _))
            {
                // Older settings files predate the floating-window flag; the new default is on.
                settings = settings with { FloatingEnabled = true };
            }

            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Returns the failure reason, or null on success.</summary>
    public string? TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this), new UTF8Encoding(false));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }
}
