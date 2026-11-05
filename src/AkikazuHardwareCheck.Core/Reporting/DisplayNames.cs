using System.Globalization;
using System.Text.RegularExpressions;

namespace AkikazuHardwareCheck.Core.Reporting;

/// <summary>Turns raw hardware strings into the short names staff and customers read. Raw values stay in the JSON record.</summary>
public static partial class DisplayNames
{
    /// <summary>Firmware often repeats the brand or model, as in "ASUS ASUS TUF Dash F15 FX517ZR_FX517ZR". Each word is kept once.</summary>
    public static string ComputerName(string? brand, string? model)
    {
        var words = $"{brand} {model}".Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The encoding older programs use on this system, by its usual name.</summary>
    public static string EncodingName(int codePage) => codePage switch
    {
        65001 => "UTF-8",
        936 => "GBK",
        932 => "Shift-JIS",
        950 => "Big5",
        949 => "EUC-KR",
        _ => $"Windows-{codePage.ToString(CultureInfo.InvariantCulture)}",
    };

    private const double BYTES_PER_GIB = 1024d * 1024 * 1024;
    private const double BYTES_PER_GB = 1000d * 1000 * 1000;
    private const double GB_PER_TB = 1000;
    private const double MBPS_PER_GBPS = 1000;
    private const int SHORT_PREFIX_LENGTH = 2;
    private const double MILLIWATT_HOURS_PER_WATT_HOUR = 1000;

    private static readonly (string Prefix, string Name)[] BoardVendors =
    [
        ("ASUSTeK", "ASUS"),
        ("Micro-Star", "MSI"),
        ("Gigabyte", "GIGABYTE"),
        ("ASRock", "ASRock"),
        ("Dell", "Dell"),
        ("Hewlett", "HP"),
        ("Lenovo", "Lenovo"),
    ];

    // Drives rarely report a vendor, so the brand comes from well-known model prefixes. Unknown models simply show none.
    private static readonly (string Prefix, string Brand)[] DiskBrands =
    [
        ("CT", "Crucial"),
        ("MTFD", "Micron"),
        ("WDC", "Western Digital"),
        ("WD", "Western Digital"),
        ("HDWD", "Toshiba"),
        ("ST", "Seagate"),
        ("SA400", "Kingston"),
        ("SKC", "Kingston"),
        ("SNV", "Kingston"),
        ("SFYRS", "Kingston"),
        ("HFS", "SK hynix"),
        ("MZ", "Samsung"),
    ];

    private static readonly (string Prefix, string Name)[] MemoryBrands =
    [
        ("G Skill", "G.Skill"),
        ("G.Skill", "G.Skill"),
        ("Kingston", "Kingston"),
        ("Corsair", "Corsair"),
        ("Crucial", "Crucial"),
        ("Micron", "Micron"),
        ("Samsung", "Samsung"),
        ("SK Hynix", "SK hynix"),
        ("Hynix", "SK hynix"),
        ("A-DATA", "ADATA"),
        ("ADATA", "ADATA"),
        ("Team", "TeamGroup"),
        ("Patriot", "Patriot"),
    ];

    private static readonly string[] BrandWords =
    [
        "Samsung", "Crucial", "Micron", "Kingston", "Seagate", "Toshiba", "KIOXIA", "SanDisk", "Intel", "ADATA",
        "Lexar", "Corsair", "Hikvision", "SK hynix", "Western Digital", "WD", "Solidigm", "TeamGroup", "Patriot", "Sabrent",
    ];

    public static string CleanModel(string model)
    {
        var cleaned = TrademarkPattern().Replace(model, "");
        cleaned = ProcessorSuffixPattern().Replace(cleaned, "");
        return SpacesPattern().Replace(cleaned, " ").Trim();
    }

    /// <summary>"Graphics" at the end of a model name says nothing the label beside it does not.</summary>
    public static string GpuModel(string model) => GraphicsSuffixPattern().Replace(CleanModel(model), "");

    /// <summary>"Intel(R) Graphics" names no particular GPU, unlike "Radeon 780M" or "UHD Graphics 770".</summary>
    public static bool IsGenericIntegratedGpu(string? model, string? vendor)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return true;
        }

        return vendor is not null && CleanModel(model).Equals($"{vendor} Graphics", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Firmware often stores a JEDEC code or padded company name. Only readable names are worth printing.</summary>
    public static string? MemoryBrand(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) || HexCodePattern().IsMatch(manufacturer.Trim()) || manufacturer.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var (prefix, name) in MemoryBrands)
        {
            if (manufacturer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return CleanModel(manufacturer);
    }

    public static string BoardVendor(string manufacturer)
    {
        foreach (var (prefix, name) in BoardVendors)
        {
            if (manufacturer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return manufacturer;
    }

    /// <summary>Returns the brand to print in front of a model, or null when the model already names it or it is unknown.</summary>
    public static string? DiskBrand(string? model)
    {
        if (string.IsNullOrWhiteSpace(model) || BrandWords.Any(word => StartsWithWord(model, word)))
        {
            return null;
        }

        foreach (var (prefix, brand) in DiskBrands)
        {
            // Two-letter prefixes are only meaningful in front of a capacity number, as in CT2000 or ST1000.
            var isMatch = model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (prefix.Length > SHORT_PREFIX_LENGTH || (model.Length > prefix.Length && char.IsDigit(model[prefix.Length])));
            if (isMatch)
            {
                return brand;
            }
        }

        return null;
    }

    /// <summary>Memory uses binary sizes, as its module sizes are powers of two.</summary>
    public static string MemorySize(ulong bytes) => $"{Number(bytes / BYTES_PER_GIB)}GB";

    /// <summary>Drives are sold in decimal sizes, so a 2,000 GB drive reads as 2TB.</summary>
    public static string DiskSize(ulong bytes)
    {
        var gigabytes = bytes / BYTES_PER_GB;
        return gigabytes >= GB_PER_TB ? $"{Number(gigabytes / GB_PER_TB)}TB" : $"{Math.Round(gigabytes).ToString(CultureInfo.InvariantCulture)}GB";
    }

    public static string LinkSpeed(long megabitsPerSecond) => megabitsPerSecond >= MBPS_PER_GBPS
        ? $"{Number(megabitsPerSecond / MBPS_PER_GBPS)}Gbps"
        : $"{megabitsPerSecond.ToString(CultureInfo.InvariantCulture)}Mbps";

    public static string PcieLink(int generation, int? lanes)
    {
        var version = $"PCIe {generation.ToString(CultureInfo.InvariantCulture)}.0";
        return lanes is { } count ? $"{version} x{count.ToString(CultureInfo.InvariantCulture)}" : version;
    }

    private static bool StartsWithWord(string text, string word) =>
        text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
        && (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]));

    public static string WattHours(ulong milliwattHours) => Number(milliwattHours / MILLIWATT_HOURS_PER_WATT_HOUR);

    private static string Number(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\((R|TM|C)\)|®|™", RegexOptions.IgnoreCase)]
    private static partial Regex TrademarkPattern();

    [GeneratedRegex(@"\s+(\d+-Core\s+)?Processor\s*$|\s+CPU\s+@.*$", RegexOptions.IgnoreCase)]
    private static partial Regex ProcessorSuffixPattern();

    [GeneratedRegex(@"(?<=\S\s\S+.*)\s+Graphics$", RegexOptions.IgnoreCase)]
    private static partial Regex GraphicsSuffixPattern();

    [GeneratedRegex(@"^[0-9A-F]{4,}$", RegexOptions.IgnoreCase)]
    private static partial Regex HexCodePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesPattern();
}
