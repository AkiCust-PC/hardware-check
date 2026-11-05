using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkikazuHardwareCheck.Core.AdvancedInfo;

/// <summary>Exports the already-built advanced snapshot. It never reads hardware, so exporting cannot disturb a test.</summary>
public static class AdvancedInfoExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(AdvancedSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    /// <summary>Returns the failure reason, or null on success.</summary>
    public static string? TrySave(AdvancedSnapshot snapshot, string path)
    {
        try
        {
            File.WriteAllText(path, Serialize(snapshot), new UTF8Encoding(false));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return exception.Message;
        }
    }
}
