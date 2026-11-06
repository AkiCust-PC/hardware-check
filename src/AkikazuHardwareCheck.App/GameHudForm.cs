using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App;

/// <summary>A lightweight top-center transparent text strip. It only reads the existing live snapshot.</summary>
internal sealed class GameHudForm : Form
{
    private readonly Translator _t;
    private readonly Func<LiveSnapshot> _readSnapshot;
    private readonly int _fontSize;
    private readonly Color _color;
    private readonly int _items;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private LiveSnapshot? _sample;

    public GameHudForm(Translator t, Func<LiveSnapshot> readSnapshot, Font font, int fontSize, Color color, int items)
    {
        _t = t;
        _readSnapshot = readSnapshot;
        _fontSize = Math.Clamp(fontSize, 8, 32);
        _color = color;
        _items = items;
        Font = font;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Fuchsia;
        TransparencyKey = Color.Fuchsia;
        DoubleBuffered = true;
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = new Rectangle(screen.Left, screen.Top, screen.Width, LogicalToDeviceUnits(42));
        _timer.Tick += (_, _) =>
        {
            _sample = _readSnapshot();
            Invalidate();
        };
        Shown += (_, _) =>
        {
            _sample = _readSnapshot();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Dispose();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0084)
        {
            message.Result = new IntPtr(-1);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_sample is not { } sample)
        {
            return;
        }

        var parts = new List<string>();
        if ((_items & 1) != 0) Add(parts, "CPU", sample.CpuLoad, "%");
        if ((_items & 2) != 0) Add(parts, string.Empty, sample.CpuTemperature, "°C");
        if ((_items & 4) != 0) Add(parts, "GPU", sample.GpuLoad, "%");
        if ((_items & 8) != 0) Add(parts, string.Empty, sample.GpuTemperature, "°C");
        if ((_items & 16) != 0) Add(parts, "MEM", sample.MemoryLoad, "%");
        if ((_items & 32) != 0)
        {
            var bounds = Screen.PrimaryScreen?.Bounds;
            if (bounds is { } area)
            {
                parts.Add($"{area.Width}x{area.Height}");
            }
        }

        if ((_items & 64) != 0)
        {
            var refresh = DisplayInfoForm.GetRefreshRate();
            if (refresh > 0)
            {
                parts.Add($"{refresh} Hz");
            }
        }
        var text = string.Join("   ", parts);
        if (text.Length == 0)
        {
            return;
        }

        using var font = new Font("Segoe UI", _fontSize, FontStyle.Bold, GraphicsUnit.Point);
        using var shadow = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        using var foreground = new SolidBrush(_color);
        var size = e.Graphics.MeasureString(text, font);
        var x = Math.Max(4f, (ClientSize.Width - size.Width) / 2f);
        e.Graphics.DrawString(text, font, shadow, x + 1, 1);
        e.Graphics.DrawString(text, font, foreground, x, 0);
    }

    private static void Add(ICollection<string> parts, string label, double? value, string unit)
    {
        if (value is { } number)
        {
            parts.Add($"{(label.Length == 0 ? string.Empty : label + " ")}{number.ToString("0", CultureInfo.InvariantCulture)}{unit}");
        }
    }
}
