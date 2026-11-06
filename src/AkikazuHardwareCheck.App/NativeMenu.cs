using System.Runtime.InteropServices;

namespace AkikazuHardwareCheck.App;

internal enum MenuCommand
{
    Report = 1,
    Copy,
    OpenReport,
    OpenFolder,
    Settings,
    RestartToFirmware,
    BackgroundColor,
    BackgroundImage,
    BackgroundAdjust,
    BackgroundReset,
    Reload,
    Exit,
    AdvancedInfo,
    Maintenance,
    JunkCleanup,
    NetworkSpeedTest,
    DisplayInfo,
    CameraTest,
    ShowHardware,
    ShowMonitor,
    ShowTests,
    Floating,
    Crosshair,
    ScreenRuler,
    GameHud,
    TestWindow,
    HardwareWindow,
    Guide,
    Updates,
    About,
    FirstLanguage = 100,
}

/// <summary>
/// The window's menu bar as a Win32 menu. Windows draws it, so it takes the look of the running Windows version.
/// WinForms' MenuStrip is drawn by WinForms itself and its older MainMenu no longer exists.
/// </summary>
internal sealed class NativeMenu
{
    private const uint MF_BYCOMMAND = 0x0000;
    private const uint MF_STRING = 0x0000;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_POPUP = 0x0010;
    private const uint MF_SEPARATOR = 0x0800;

    private readonly IntPtr _bar = CreateMenu();

    public IntPtr AddSubmenu(string text) => AddPopup(_bar, text);

    /// <summary>A submenu below another submenu, for tools that belong inside the Tools menu.</summary>
    public static IntPtr AddPopup(IntPtr parent, string text)
    {
        var submenu = CreatePopupMenu();
        AppendMenuW(parent, MF_POPUP, submenu, text);
        return submenu;
    }

    /// <summary>A command directly on the menu bar, used by Settings so it does not become a one-item submenu.</summary>
    public void AddBarItem(MenuCommand command, string text) =>
        AppendMenuW(_bar, MF_STRING, (int)command, text);

    public static void AddItem(IntPtr submenu, MenuCommand command, string text) =>
        AppendMenuW(submenu, MF_STRING, (IntPtr)(int)command, text);

    public static void AddSeparator(IntPtr submenu) =>
        AppendMenuW(submenu, MF_SEPARATOR, IntPtr.Zero, null);

    public void SetEnabled(MenuCommand command, bool isEnabled) =>
        EnableMenuItem(_bar, (uint)command, MF_BYCOMMAND | (isEnabled ? MF_STRING : MF_GRAYED));

    public void SetChecked(MenuCommand command, bool isChecked) =>
        CheckMenuItem(_bar, (uint)command, MF_BYCOMMAND | (isChecked ? MF_CHECKED : MF_STRING));

    public void SetRadio(MenuCommand first, int count, MenuCommand chosen) =>
        CheckMenuRadioItem(_bar, (uint)first, (uint)first + (uint)count - 1, (uint)chosen, MF_BYCOMMAND);

    /// <summary>Replaces the window's menu. Windows frees a menu along with its window, so only a replaced one is destroyed here.</summary>
    public void Attach(IntPtr window, NativeMenu? previous)
    {
        SetMenu(window, _bar);
        DrawMenuBar(window);
        if (previous is not null)
        {
            DestroyMenu(previous._bar);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreateMenu();

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenu(IntPtr window, IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool DrawMenuBar(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint CheckMenuItem(IntPtr menu, uint id, uint flags);

    [DllImport("user32.dll")]
    private static extern int EnableMenuItem(IntPtr menu, uint id, uint flags);

    [DllImport("user32.dll")]
    private static extern bool CheckMenuRadioItem(IntPtr menu, uint first, uint last, uint chosen, uint flags);
}
