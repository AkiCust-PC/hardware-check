using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Reads the Windows version and activation state. Queries never select the product key.</summary>
public static class OperatingSystemCollector
{
    private const string CURRENT_VERSION_KEY = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    // Application ID of Windows in the Software Licensing Service.
    private const string WINDOWS_APPLICATION_ID = "55c92734-d682-4d71-983e-d6ec3f16059f";

    // SoftwareLicensingProduct.LicenseStatus values from the Software Licensing documentation.
    private const int LICENSE_UNLICENSED = 0;
    private const int LICENSE_LICENSED = 1;
    private const int LICENSE_OOB_GRACE = 2;
    private const int LICENSE_OOT_GRACE = 3;
    private const int LICENSE_NON_GENUINE_GRACE = 4;
    private const int LICENSE_NOTIFICATION = 5;
    private const int LICENSE_EXTENDED_GRACE = 6;

    public static OperatingSystemInfo ReadOperatingSystem()
    {
        // ProductName in the registry still says "Windows 10" on Windows 11, so the name comes from WMI.
        var name = Wmi.Query(Wmi.CIM_SCOPE, "SELECT Caption FROM Win32_OperatingSystem", item => Wmi.Text(item, "Caption"))
            .FirstOrDefault();

        using var key = Registry.LocalMachine.OpenSubKey(CURRENT_VERSION_KEY);
        return new OperatingSystemInfo(
            name,
            key?.GetValue("DisplayVersion") as string,
            FormatBuild(key?.GetValue("CurrentBuildNumber") as string, key?.GetValue("UBR") as int?),
            RuntimeInformation.OSArchitecture.ToString());
    }

    public static WindowsActivation ReadActivation()
    {
        var statuses = Wmi.Query(
            Wmi.CIM_SCOPE,
            $"SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '{WINDOWS_APPLICATION_ID}' AND PartialProductKey IS NOT NULL",
            item => Wmi.Integer(item, "LicenseStatus"));

        return MapActivation(statuses);
    }

    public static WindowsActivation MapActivation(IReadOnlyCollection<int?> statuses)
    {
        if (statuses.Contains(LICENSE_LICENSED))
        {
            return WindowsActivation.Activated;
        }

        if (statuses.Any(status => status is LICENSE_OOB_GRACE or LICENSE_OOT_GRACE or LICENSE_NON_GENUINE_GRACE or LICENSE_EXTENDED_GRACE))
        {
            return WindowsActivation.GracePeriod;
        }

        if (statuses.Contains(LICENSE_NOTIFICATION))
        {
            return WindowsActivation.Notification;
        }

        return statuses.Contains(LICENSE_UNLICENSED) ? WindowsActivation.NotActivated : WindowsActivation.Unknown;
    }

    public static string? FormatBuild(string? build, int? updateRevision) =>
        build is null || updateRevision is null ? build : $"{build}.{updateRevision}";
}
