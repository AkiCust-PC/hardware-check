using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Reads TPM and Secure Boot state, which Windows 11 requires. Neither source needs administrator rights.</summary>
public static class SecurityCollector
{
    private const string SECURE_BOOT_KEY = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string SECURE_BOOT_VALUE = "UEFISecureBootEnabled";
    private const uint TPM_DEVICE_INFO_VERSION = 1;
    private const uint TBS_SUCCESS = 0;

    public static SecurityInfo Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SECURE_BOOT_KEY);

        // The key is absent on PCs booted in legacy BIOS mode, where the feature does not exist.
        var secureBoot = key?.GetValue(SECURE_BOOT_VALUE) is int value ? value != 0 : (bool?)null;
        return new SecurityInfo(secureBoot, ReadTpmVersion());
    }

    /// <summary>TPM_DEVICE_INFO reports 1 for TPM 1.2 and 2 for TPM 2.0.</summary>
    public static string? MapTpmVersion(uint version) => version switch
    {
        1 => "1.2",
        2 => "2.0",
        _ => null,
    };

    private static string? ReadTpmVersion()
    {
        var info = new TpmDeviceInfo { StructVersion = TPM_DEVICE_INFO_VERSION };
        return Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TpmDeviceInfo>(), ref info) == TBS_SUCCESS ? MapTpmVersion(info.TpmVersion) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TpmDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    [DllImport("tbs.dll")]
    private static extern uint Tbsi_GetDeviceInfo(uint size, ref TpmDeviceInfo info);
}
