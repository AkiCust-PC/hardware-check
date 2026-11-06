using System.Globalization;
using System.Text;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Writes one line per startup stage. A fast-fail (0xC0000409) leaves no managed stack, so the last recorded stage is
/// the only way to tell whether the crash happened before, during or after the main window was created.
/// </summary>
internal static class StartupTrace
{
    private const string FOLDER_NAME = "AkikazuHardwareCheck";
    private const string FILE_NAME = "startup.log";
    private const long MAX_BYTES = 64 * 1024;

    private static readonly object Gate = new();

    public static string? LastStage { get; private set; }

    public static string PathName { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FOLDER_NAME,
        FILE_NAME);

    public static void Record(string stage)
    {
        LastStage = stage;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
                var file = new FileInfo(PathName);
                if (file.Exists && file.Length > MAX_BYTES)
                {
                    file.Delete();
                }

                File.AppendAllText(
                    PathName,
                    $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} {stage}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch
            {
                // Diagnostics must never be the reason a startup fails.
            }
        }
    }
}
