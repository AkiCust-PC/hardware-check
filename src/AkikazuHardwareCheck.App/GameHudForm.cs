using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// A lightweight top-center transparent text strip. It only reads the existing live snapshot; it never opens a
/// sensor source and never touches a game process.
/// </summary>
internal sealed class GameHudForm : Form
{
    public const int CPU_LOAD = 1 << 0;
    public const int CPU_TEMPERATURE = 1 << 1;
    public const int GPU_LOAD = 1 << 2;
    public const int GPU_TEMPERATURE = 1 << 3;
    public const int MEMORY = 1 << 4;
    public const int RESOLUTION = 1 << 5;
    public const int REFRESH_RATE = 1 << 6;
    public const int CPU_CLOCK = 1 << 7;
    public const int CPU_POWER = 1 << 8;
    public const int GPU_CLOCK = 1 << 9;
    public const int GPU_POWER = 1 << 10;
    public const int GPU_MEMORY = 1 << 11;
    public const int DISK = 1 << 12;
    public const int LATENCY = 1 << 13;

    private const int BYTES_PER_GIB = 1024 * 1024 * 1024;
    private const int HUD_HEIGHT = 42;

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
        Bounds = new Rectangle(screen.Left, screen.Top, screen.Width, LogicalToDeviceUnits(HUD_HEIGHT));
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
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
        AddPercent(parts, CPU_LOAD, _t["hud.label.cpu"], sample.CpuLoad);
        AddValue(parts, CPU_TEMPERATURE, _t["hud.label.cpu"], sample.CpuTemperature, "0", "°C");
        AddClock(parts, CPU_CLOCK, _t["hud.label.cpu"], sample.CpuClockMhz);
        AddValue(parts, CPU_POWER, _t["hud.label.cpu"], sample.CpuPowerWatts, "0", "W");
        AddPercent(parts, GPU_LOAD, _t["hud.label.gpu"], sample.GpuLoad);
        AddValue(parts, GPU_TEMPERATURE, _t["hud.label.gpu"], sample.GpuTemperature, "0", "°C");
        AddClock(parts, GPU_CLOCK, _t["hud.label.gpu"], sample.GpuClockMhz);
        AddValue(parts, GPU_POWER, _t["hud.label.gpu"], sample.GpuPowerWatts, "0", "W");
        AddPercent(parts, MEMORY, _t["hud.label.memory"], sample.MemoryLoad);
        AddMemory(parts, sample);
        AddPercent(parts, DISK, _t["hud.label.disk"], sample.DiskBusy);
        AddValue(parts, LATENCY, _t["hud.label.latency"], sample.LatencyMilliseconds, "0", "ms");
        if ((_items & RESOLUTION) != 0 && Screen.PrimaryScreen?.Bounds is { } area)
        {
            parts.Add($"{area.Width}x{area.Height}");
        }

        if ((_items & REFRESH_RATE) != 0)
        {
            var refresh = DisplayInfoForm.GetRefreshRate();
            if (refresh > 0)
            {
                parts.Add($"{refresh.ToString(CultureInfo.InvariantCulture)}Hz");
            }
        }

        var text = string.Join("   ", parts);
        if (text.Length == 0)
        {
            return;
        }

        using var font = FitFont(e.Graphics, text);
        using var shadow = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        using var foreground = new SolidBrush(_color);
        var size = e.Graphics.MeasureString(text, font);
        var x = Math.Max(4f, (ClientSize.Width - size.Width) / 2f);
        e.Graphics.DrawString(text, font, shadow, x + 1, 1);
        e.Graphics.DrawString(text, font, foreground, x, 0);
    }

    /// <summary>Shrinks the strip when the selected items are wider than the screen instead of cutting either end.</summary>
    private Font FitFont(Graphics graphics, string text)
    {
        var size = (float)_fontSize;
        var available = Math.Max(1, ClientSize.Width - 8);
        while (size > 7f)
        {
            var font = new Font(Font.FontFamily, size, FontStyle.Bold, GraphicsUnit.Point);
            if (graphics.MeasureString(text, font).Width <= available)
            {
                return font;
            }

            font.Dispose();
            size -= 0.5f;
        }

        return new Font(Font.FontFamily, 7f, FontStyle.Bold, GraphicsUnit.Point);
    }

    private void AddPercent(ICollection<string> parts, int item, string label, double? value)
    {
        if ((_items & item) != 0 && value is { } number)
        {
            parts.Add($"{label} {number.ToString("0", CultureInfo.InvariantCulture)}%");
        }
    }

    private void AddValue(ICollection<string> parts, int item, string label, double? value, string format, string unit)
    {
        if ((_items & item) != 0 && value is { } number)
        {
            parts.Add($"{label} {number.ToString(format, CultureInfo.InvariantCulture)}{unit}");
        }
    }

    private void AddClock(ICollection<string> parts, int item, string label, double? megahertz)
    {
        if ((_items & item) != 0 && megahertz is { } value and > 0)
        {
            parts.Add($"{label} {(value / 1000).ToString("0.0", CultureInfo.InvariantCulture)}GHz");
        }
    }

    private void AddMemory(ICollection<string> parts, LiveSnapshot sample)
    {
        if ((_items & GPU_MEMORY) == 0)
        {
            return;
        }

        if (sample.GpuMemoryUsedBytes is { } used && sample.GpuMemoryTotalBytes is { } total and > 0)
        {
            parts.Add($"{_t["hud.label.vram"]} {(used / total * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        }
        else if (sample.GpuMemoryUsedBytes is { } usedBytes)
        {
            parts.Add($"{_t["hud.label.vram"]} {(usedBytes / BYTES_PER_GIB).ToString("0.0", CultureInfo.InvariantCulture)}GB");
        }
    }
}
