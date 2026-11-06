using AkikazuHardwareCheck.Core.Localization;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Repairs the Windows hardware keyboard type when a Japanese 106-key keyboard is being treated as an English
/// 101-key keyboard. Windows reads these values from i8042prt at boot, so a restart is required.
/// </summary>
internal static class KeyboardLayoutFix
{
    private const string PARAMETERS_KEY = @"SYSTEM\CurrentControlSet\Services\i8042prt\Parameters";
    private const string JAPANESE_DRIVER = "kbd106.dll";
    private const string ENGLISH_DRIVER = "kbd101.dll";

    public static string Current(Translator t)
    {
        using var key = Registry.LocalMachine.OpenSubKey(PARAMETERS_KEY);
        return (key?.GetValue("LayerDriverJPN") as string) switch
        {
            JAPANESE_DRIVER => t["keyboardFix.currentJapanese"],
            ENGLISH_DRIVER => t["keyboardFix.currentEnglish"],
            _ => t["keyboardFix.currentUnknown"],
        };
    }

    public static void ApplyJapanese106()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PARAMETERS_KEY, writable: true)
            ?? throw new InvalidOperationException(PARAMETERS_KEY);
        key.SetValue("LayerDriverJPN", JAPANESE_DRIVER, RegistryValueKind.String);
        key.SetValue("OverrideKeyboardIdentifier", "PCAT_106KEY", RegistryValueKind.String);
        key.SetValue("OverrideKeyboardSubtype", 2, RegistryValueKind.DWord);
        key.SetValue("OverrideKeyboardType", 7, RegistryValueKind.DWord);
    }
}
