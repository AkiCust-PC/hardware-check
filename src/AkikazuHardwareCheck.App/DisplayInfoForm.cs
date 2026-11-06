using System.Globalization;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Reads the current primary display mode without changing it.</summary>
internal sealed class DisplayInfoForm : Form
{
    private const int ENUM_CURRENT_SETTINGS = -1;

    public DisplayInfoForm(Translator t, Font font)
    {
        Text = t["displayInfo.title"];
        Icon = Ui.AppIcon;
        Font = font;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Ui.SPACING * 2);

        var screen = Screen.PrimaryScreen;
        var width = screen?.Bounds.Width ?? 0;
        var height = screen?.Bounds.Height ?? 0;
        var refresh = GetRefreshRate();
        var dpi = DeviceDpi;

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(180)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Add(table, 0, t["displayInfo.resolution"], width > 0 ? $"{width} × {height}" : "—");
        Add(table, 1, t["displayInfo.refresh"], refresh > 0 ? $"{refresh.ToString(CultureInfo.InvariantCulture)} Hz" : "—");
        Add(table, 2, t["displayInfo.dpi"], $"{dpi} DPI");
        Add(table, 3, t["displayInfo.scale"], $"{(dpi * 100d / 96d).ToString("0", CultureInfo.InvariantCulture)}%");

        var close = Ui.Button();
        close.Text = t["ui.ok"];
        close.DialogResult = DialogResult.OK;
        var buttons = Ui.Flow();
        buttons.Controls.Add(close);
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
        root.Controls.Add(table, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        Ui.ScaleButtons(this);
    }

    private static void Add(TableLayoutPanel table, int row, string label, string value)
    {
        table.RowCount++;
        table.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, Ui.SPACING) }, 0, row);
        table.Controls.Add(new Label { Text = value, AutoSize = true, Margin = new Padding(0, 0, 0, Ui.SPACING) }, 1, row);
    }

    internal static int GetRefreshRate()
    {
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode) ? mode.dmDisplayFrequency : 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);
}
