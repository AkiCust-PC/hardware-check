namespace AkikazuHardwareCheck.Core.Localization;

public static class AppLanguage
{
    public const string JAPANESE = "ja-JP";
    public const string SIMPLIFIED_CHINESE = "zh-CN";
    public const string TRADITIONAL_CHINESE = "zh-TW";
    public const string ENGLISH = "en-US";

    public static readonly IReadOnlyList<string> Supported = [JAPANESE, SIMPLIFIED_CHINESE, TRADITIONAL_CHINESE, ENGLISH];

    private static readonly HashSet<string> TraditionalChineseSubtags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hant",
        "TW",
        "HK",
        "MO",
    };

    /// <summary>Maps a system culture name to a supported language. Languages outside the four fall back to English.</summary>
    public static string FromCulture(string cultureName)
    {
        var subtags = cultureName.Split('-');
        return subtags[0].ToLowerInvariant() switch
        {
            "ja" => JAPANESE,
            "zh" when subtags.Contains("Hans", StringComparer.OrdinalIgnoreCase) => SIMPLIFIED_CHINESE,
            "zh" when subtags.Skip(1).Any(TraditionalChineseSubtags.Contains) => TRADITIONAL_CHINESE,
            "zh" => SIMPLIFIED_CHINESE,
            _ => ENGLISH,
        };
    }

    public static string? Parse(string code) =>
        Supported.FirstOrDefault(language => language.Equals(code, StringComparison.OrdinalIgnoreCase));
}
