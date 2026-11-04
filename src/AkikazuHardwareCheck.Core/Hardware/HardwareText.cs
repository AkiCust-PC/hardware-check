namespace AkikazuHardwareCheck.Core.Hardware;

public static class HardwareText
{
    // Placeholder strings that firmware vendors leave in SMBIOS fields: these mean "not provided".
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default string",
        "None",
        "Not Available",
        "Not Specified",
        "O.E.M.",
        "System Product Name",
        "To Be Filled By O.E.M.",
        "Unknown",
    };

    public static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || Placeholders.Contains(trimmed) ? null : trimmed;
    }

    /// <summary>Decodes the null-padded UTF-16 code unit arrays used by the WmiMonitorID class.</summary>
    public static string? DecodeCodeUnits(ushort[]? codeUnits)
    {
        if (codeUnits is null)
        {
            return null;
        }

        return Clean(new string(codeUnits.TakeWhile(unit => unit != 0).Select(unit => (char)unit).ToArray()));
    }

    /// <summary>
    /// Brings a display config device path and a WMI monitor instance name to one comparable key, as in
    /// <c>\\?\DISPLAY#GSM5B7F#5&amp;a&amp;UID1#{guid}</c> and <c>DISPLAY\GSM5B7F\5&amp;a&amp;UID1_0</c>.
    /// </summary>
    public static string? MonitorKey(string? devicePathOrInstance)
    {
        if (string.IsNullOrWhiteSpace(devicePathOrInstance))
        {
            return null;
        }

        var key = devicePathOrInstance.Trim();
        if (key.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            key = key[4..];
        }

        var interfaceGuid = key.IndexOf("#{", StringComparison.Ordinal);
        if (interfaceGuid >= 0)
        {
            key = key[..interfaceGuid];
        }

        key = key.Replace('#', '\\');

        var instanceSuffix = key.LastIndexOf('_');
        if (instanceSuffix > key.LastIndexOf('\\'))
        {
            key = key[..instanceSuffix];
        }

        return key.ToUpperInvariant();
    }

    public static (string? ManufacturerCode, string? ProductCode) ParseMonitorIds(string? monitorKey)
    {
        var parts = monitorKey?.Split('\\') ?? [];
        if (parts.Length < 2 || parts[1].Length != "AAA0000".Length)
        {
            return (null, null);
        }

        return (parts[1][..3], parts[1][3..]);
    }
}
