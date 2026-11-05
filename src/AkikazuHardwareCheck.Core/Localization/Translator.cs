using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AkikazuHardwareCheck.Core.Localization;

/// <summary>Holds one language's text from the embedded <c>Localization/*.json</c> files.</summary>
public sealed class Translator
{
    private readonly IReadOnlyDictionary<string, string> _messages;

    private Translator(string language, IReadOnlyDictionary<string, string> messages)
    {
        Language = language;
        _messages = messages;
    }

    public string Language { get; }

    public IEnumerable<string> Keys => _messages.Keys;

    public static Translator Load(string language)
    {
        var resourceName = $"i18n.{language}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Unsupported language: {language}", nameof(language));
        var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Empty language file: {resourceName}");
        return new Translator(language, messages);
    }

    /// <summary>An unknown key throws: tests render every report branch in every language to catch it before release.</summary>
    public string this[string key, params object?[] args] =>
        args.Length == 0
            ? _messages[key]
            : string.Format(CultureInfo.InvariantCulture, _messages[key], args);
}
