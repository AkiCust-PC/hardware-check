using System.Globalization;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Reads the Windows language settings. The system locale and code page decide how older programs show text.</summary>
public static class LocaleCollector
{
    private const int LOCALE_NAME_MAX_LENGTH = 85;

    public static LocaleInfo Read()
    {
        var buffer = new char[LOCALE_NAME_MAX_LENGTH];
        var length = GetSystemDefaultLocaleName(buffer, buffer.Length);

        // The length includes the terminating zero; zero means the call failed.
        var systemLocale = length > 0 ? new string(buffer, 0, length - 1) : CultureInfo.InstalledUICulture.Name;
        return new LocaleInfo(CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentCulture.Name, systemLocale, (int)GetACP());
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetSystemDefaultLocaleName([Out] char[] name, int length);
}
