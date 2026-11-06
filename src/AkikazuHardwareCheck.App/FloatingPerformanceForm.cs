using System.Drawing.Drawing2D;
using System.Globalization;
using AkikazuHardwareCheck.Core.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// A borderless performance window. Near a screen edge it is a small vertical strip; away from the edge it becomes a
/// semi-transparent circular ball showing only the CPU/GPU temperatures. A click on the surface trims other processes.
/// </summary>
internal sealed class FloatingPerformanceForm : Form
{
    private const int BALL_SIZE = 132;
    private const int STRIP_WIDTH = 220;
    private const int STRIP_HEIGHT = 320;
    private const int SNAP_DISTANCE = 28;
    private const int DRAG_THRESHOLD = 4;

    private readonly Translator _t;
    private readonly Func<LiveSnapshot> _readSnapshot;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private Point _dragCursor;
    private Point _dragStart;
    private bool _dragging;
    private bool _dragMoved;
    private bool _snapped = true;
    private bool _cleaning;
    private LiveSnapshot? _sample;
    private string? _status;
    private DateTimeOffset _statusUntil;

    public FloatingPerformanceForm(Translator t, Func<LiveSnapshot> readSnapshot, Font font)
    {
        _t = t;
        _readSnapshot = readSnapshot;
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["floating.title"];
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(18, 23, 30);
        DoubleBuffered = true;
        ClientSize = new Size(BALL_SIZE, BALL_SIZE);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);

        _timer.Tick += (_, _) => RefreshSample();
        Shown += (_, _) =>
        {
            ApplyMode();
            SnapToNearestEdge(force: true);
            RefreshSample();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Dispose();
        MouseDown += BeginDrag;
        MouseMove += MoveDrag;
        MouseUp += EndDrag;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        if (_snapped)
        {
            DrawStrip(e.Graphics);
        }
        else
        {
            DrawBall(e.Graphics);
        }
    }

    private void RefreshSample()
    {
        _sample = _readSnapshot();
        if (_status is not null && DateTimeOffset.Now >= _statusUntil)
        {
            _status = null;
        }

        Invalidate();
    }

    private void ApplyMode()
    {
        var size = _snapped
            ? new Size(DpiScale(STRIP_WIDTH), DpiScale(STRIP_HEIGHT))
            : new Size(DpiScale(BALL_SIZE), DpiScale(BALL_SIZE));
        ClientSize = size;
        Opacity = _snapped ? 0.94 : 0.86;
        if (_snapped)
        {
            Region?.Dispose();
            Region = null;
        }
        else
        {
            SetEllipseRegion(size);
        }

        Invalidate();
    }

    private void SetEllipseRegion(Size size)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, Math.Max(1, size.Width - 1), Math.Max(1, size.Height - 1));
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private void DrawBall(Graphics graphics)
    {
        var bounds = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 3), Math.Max(1, ClientSize.Height - 3));
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(235, 43, 55, 72), Color.FromArgb(240, 16, 22, 31), 45f);
        graphics.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(110, 255, 255, 255), 1f);
        graphics.DrawPath(border, path);

        using var labelFont = new Font(Font.FontFamily, 8.5f, FontStyle.Regular);
        using var valueFont = new Font(Font.FontFamily, 17f, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Color.FromArgb(190, 220, 228, 238));
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        var labelRect = new RectangleF(0, ClientSize.Height * 0.24f, ClientSize.Width, ClientSize.Height * 0.2f);
        graphics.DrawString(_t["floating.ballLabel"], labelFont, muted, labelRect, centered);

        var temperature = TemperaturePair(_sample?.CpuTemperature, _sample?.GpuTemperature);
        var valueRect = new RectangleF(0, ClientSize.Height * 0.42f, ClientSize.Width, ClientSize.Height * 0.32f);
        graphics.DrawString(temperature, valueFont, white, valueRect, centered);

        if (_cleaning)
        {
            using var small = new Font(Font.FontFamily, 8f, FontStyle.Regular);
            graphics.DrawString(_t["floating.cleaning"], small, muted, new RectangleF(0, ClientSize.Height * 0.72f, ClientSize.Width, ClientSize.Height * 0.18f), centered);
        }
    }

    private void DrawStrip(Graphics graphics)
    {
        var bounds = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 3), Math.Max(1, ClientSize.Height - 3));
        using var path = RoundedRect(bounds, DpiScale(12));
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(238, 36, 44, 58), Color.FromArgb(240, 15, 20, 28), 90f);
        graphics.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(95, 255, 255, 255), 1f);
        graphics.DrawPath(border, path);

        using var titleFont = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        using var labelFont = new Font(Font.FontFamily, 8f, FontStyle.Regular);
        using var valueFont = new Font(Font.FontFamily, 13f, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Color.FromArgb(190, 220, 228, 238));
        using var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        var padding = DpiScale(14);
        graphics.DrawString(_t["floating.title"], titleFont, white, new RectangleF(padding, DpiScale(12), ClientSize.Width - (padding * 2), DpiScale(20)));
        var y = DpiScale(49);
        DrawRow(graphics, _t["floating.cpuTemperature"], Temperature(_sample?.CpuTemperature), labelFont, valueFont, white, muted, left, right, padding, ref y);
        DrawRow(graphics, _t["floating.gpuTemperature"], Temperature(_sample?.GpuTemperature), labelFont, valueFont, white, muted, left, right, padding, ref y);
        DrawRow(graphics, _t["floating.cpuLoad"], Percent(_sample?.CpuLoad), labelFont, valueFont, white, muted, left, right, padding, ref y);
        DrawRow(graphics, _t["floating.gpuLoad"], Percent(_sample?.GpuLoad), labelFont, valueFont, white, muted, left, right, padding, ref y);
        DrawRow(graphics, _t["floating.memory"], Percent(_sample?.MemoryLoad), labelFont, valueFont, white, muted, left, right, padding, ref y);

        using var hintFont = new Font(Font.FontFamily, 8f, FontStyle.Regular);
        graphics.DrawString(_t["floating.clickHint"], hintFont, muted, new RectangleF(padding, ClientSize.Height - DpiScale(34), ClientSize.Width - (padding * 2), DpiScale(22)), new StringFormat { Alignment = StringAlignment.Center });
        if (_status is not null)
        {
            graphics.DrawString(_status, hintFont, white, new RectangleF(padding, ClientSize.Height - DpiScale(56), ClientSize.Width - (padding * 2), DpiScale(20)), new StringFormat { Alignment = StringAlignment.Center });
        }
    }

    private void DrawRow(Graphics graphics, string label, string value, Font labelFont, Font valueFont, Brush white, Brush muted, StringFormat left, StringFormat right, int padding, ref int y)
    {
        var rowHeight = DpiScale(34);
        graphics.DrawString(label, labelFont, muted, new RectangleF(padding, y, ClientSize.Width - (padding * 2), rowHeight), left);
        graphics.DrawString(value, valueFont, white, new RectangleF(padding, y, ClientSize.Width - (padding * 2), rowHeight), right);
        y += rowHeight;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragMoved = false;
        _dragCursor = Cursor.Position;
        _dragStart = Location;
    }

    private void MoveDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var deltaX = Cursor.Position.X - _dragCursor.X;
        var deltaY = Cursor.Position.Y - _dragCursor.Y;
        if (Math.Abs(deltaX) >= DpiScale(DRAG_THRESHOLD) || Math.Abs(deltaY) >= DpiScale(DRAG_THRESHOLD))
        {
            _dragMoved = true;
        }

        Location = new Point(_dragStart.X + deltaX, _dragStart.Y + deltaY);
    }

    private void EndDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (_dragMoved)
        {
            SnapToNearestEdge();
            return;
        }

        _ = CleanMemoryAsync();
    }

    private void SnapToNearestEdge(bool force = false)
    {
        var screen = Screen.FromControl(this).WorkingArea;
        var leftDistance = Math.Abs(Left - screen.Left);
        var rightDistance = Math.Abs(screen.Right - Right);
        var nearEdge = Math.Min(leftDistance, rightDistance) <= DpiScale(SNAP_DISTANCE);
        _snapped = force || nearEdge;
        ApplyMode();
        if (!_snapped)
        {
            return;
        }

        Left = rightDistance <= leftDistance ? screen.Right - Width : screen.Left;
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
    }

    private async Task CleanMemoryAsync()
    {
        if (_cleaning)
        {
            return;
        }

        if (MessageBox.Show(this, _t["floating.confirm"], _t["floating.clean"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _cleaning = true;
        _status = _t["floating.cleaning"];
        Invalidate();
        try
        {
            var result = await Task.Run(MemoryOptimizer.CleanOtherProcesses);
            _status = _t["floating.done", result.Succeeded, result.Skipped];
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            _status = exception.Message;
        }
        finally
        {
            _cleaning = false;
            _statusUntil = DateTimeOffset.Now.AddSeconds(4);
            Invalidate();
        }
    }

    private int DpiScale(int logical) => (int)Math.Round(logical * DeviceDpi / 96d);

    private static string Temperature(double? value) =>
        value is { } number ? $"{number.ToString("0.#", CultureInfo.InvariantCulture)} °C" : "—";

    private static string Percent(double? value) =>
        value is { } number ? $"{number.ToString("0", CultureInfo.InvariantCulture)}%" : "—";

    private static string TemperaturePair(double? cpu, double? gpu) =>
        $"{ShortTemperature(cpu)}/{ShortTemperature(gpu)}°C";

    private static string ShortTemperature(double? value) =>
        value is { } number ? number.ToString("0", CultureInfo.InvariantCulture) : "—";
}
