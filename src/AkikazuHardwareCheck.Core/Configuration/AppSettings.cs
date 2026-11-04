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
public sealed record AppSettings(
    string? Language = null,
    string? ReportFolder = null,
    double? GpuStressSeconds = null,
    double? CpuStressSeconds = null,
    double? CpuProtectionCelsius = null,
    double? GpuProtectionCelsius = null,
    bool IsModelsOnlyView = false)
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
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
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
