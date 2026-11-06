using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// The physical keyboard layouts staff actually switch between. Windows reports the input language, not the key
/// layout, so a Japanese 106/109 keyboard can sit behind an English input language; loading a KLID changes the keys
/// themselves instead of only the IME language.
/// </summary>
internal static class KeyboardLayouts
{
    public sealed record Choice(string Klid, string LabelKey)
    {
        public string Label(Translator t) => t[LabelKey];
    }

    public sealed record Result(Choice Choice, bool IsActivated, bool IsSystemDefault);

    public static IReadOnlyList<Choice> Choices { get; } =
    [
        new("00000411", "keyboard.japanese"),
        new("00000409", "keyboard.englishUs"),
        new("00000809", "keyboard.englishUk"),
        new("00000804", "keyboard.chineseSimplified"),
        new("00000404", "keyboard.chineseTraditional"),
        new("00000412", "keyboard.korean"),
    ];

    public static int CurrentIndex()
    {
        var languageId = (uint)(GetKeyboardLayout(0).ToInt64() & 0xFFFF);
        return Choices.ToList().FindIndex(choice => LanguageId(choice.Klid) == languageId);
    }

    public static Result Apply(Choice choice)
    {
        var handle = LoadKeyboardLayoutW(choice.Klid, KLF_ACTIVATE);
        if (handle == IntPtr.Zero)
        {
            return new Result(choice, false, false);
        }

        var systemDefault = SystemParametersInfoW(SPI_SETDEFAULTINPUTLANG, 0, ref handle, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        var activated = ActivateKeyboardLayout(handle, KLF_SETFORPROCESS) != IntPtr.Zero;
        // Ask the foreground program to switch too; Windows may still keep a per-app layout when the user set one.
        PostMessageW(HWND_BROADCAST, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, handle);
        return new Result(choice, activated, systemDefault);
    }

    private static uint LanguageId(string klid) => Convert.ToUInt32(klid[4..], 16);

    private const uint KLF_ACTIVATE = 0x00000001;
    private const uint KLF_SETFORPROCESS = 0x00000100;
    private const uint SPI_SETDEFAULTINPUTLANG = 0x005A;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayoutW(string klid, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr layout, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref IntPtr value, uint flags);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
