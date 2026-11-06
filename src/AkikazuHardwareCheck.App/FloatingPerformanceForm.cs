using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// A borderless performance window. It docks to a screen edge as a neutral glass strip, or becomes a small circular
/// glass ball away from the edge. The ball shows only CPU temperature when available and memory percentage.
/// </summary>
internal sealed class FloatingPerformanceForm : Form
{
    private const int BALL_SIZE = 96;
    private const int STRIP_WIDTH = 26;
    private const int STRIP_HEIGHT = 150;
    private const int SNAP_DISTANCE = 28;
    private const int DRAG_THRESHOLD = 4;
    private const int ANIMATION_INTERVAL_MS = 50;
    private const int SAMPLE_INTERVAL_MS = 1000;
    private const int CLICK_DELAY_MS = 240;

    private readonly Translator _t;
    private readonly Func<LiveSnapshot> _readSnapshot;
    private readonly System.Windows.Forms.Timer _sampleTimer = new() { Interval = SAMPLE_INTERVAL_MS };
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = ANIMATION_INTERVAL_MS };
    private readonly System.Windows.Forms.Timer _clickTimer = new() { Interval = CLICK_DELAY_MS };
    private readonly ContextMenuStrip _menu = new();
    private Point _dragCursor;
    private Point _dragStart;
    private readonly int? _initialLeft;
    private readonly int? _initialTop;
    private bool _dragging;
    private bool _dragMoved;
    private bool _snapped;
    private bool _infoVisible = true;
    private bool _cleaning;
    private bool _clickPending;
    private double _animationPhase;
    private LiveSnapshot? _sample;
    private string? _status;
    private DateTimeOffset _statusUntil;

    public FloatingPerformanceForm(
        Translator t,
        Func<LiveSnapshot> readSnapshot,
        Font font,
        int? initialLeft = null,
        int? initialTop = null,
        bool snapped = false,
        bool infoVisible = true)
    {
        _t = t;
        _readSnapshot = readSnapshot;
        _initialLeft = initialLeft;
        _initialTop = initialTop;
        _snapped = snapped;
        _infoVisible = infoVisible;
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["floating.title"];
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        DoubleBuffered = true;
        ClientSize = new Size(BALL_SIZE, BALL_SIZE);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        BuildMenu();
        _sampleTimer.Tick += (_, _) => RefreshSample();
        _animationTimer.Tick += (_, _) =>
        {
            _animationPhase += 0.16;
            Render();
        };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            _clickPending = false;
            _infoVisible = !_infoVisible;
            Render();
        };
        Shown += (_, _) =>
        {
            ApplyMode();
            if (_initialLeft is { } left && _initialTop is { } top)
            {
                Location = new Point(left, top);
                ClampToWorkingArea();
            }
            else if (_snapped)
            {
                SnapToNearestEdge(force: true);
            }
            else
            {
                PositionInitialBall();
            }

            EnsureVisible();
            RefreshSample();
            _sampleTimer.Start();
            _animationTimer.Start();
        };
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                _sampleTimer.Start();
                _animationTimer.Start();
            }
            else
            {
                _sampleTimer.Stop();
                _animationTimer.Stop();
            }
        };
        FormClosed += (_, _) =>
        {
            _sampleTimer.Dispose();
            _animationTimer.Dispose();
            _clickTimer.Dispose();
            _menu.Dispose();
        };

        MouseDown += BeginDrag;
        MouseMove += MoveDrag;
        MouseUp += EndDrag;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnableLayered();
    }

    private void BuildMenu()
    {
        AddMenuItem(_t["floating.dockLeft"], () => DockTo(left: true));
        AddMenuItem(_t["floating.dockRight"], () => DockTo(left: false));
        AddMenuItem(_t["floating.undock"], Undock);
        _menu.Items.Add(new ToolStripSeparator());
        AddMenuItem(_t["floating.hide"], Hide);
    }

    private void AddMenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
    }

    private void RefreshSample()
    {
        _sample = _readSnapshot();
        if (_status is not null && DateTimeOffset.Now >= _statusUntil)
        {
            _status = null;
        }

        Render();
    }

    private void ApplyMode()
    {
        var size = _snapped
            ? new Size(DpiScale(STRIP_WIDTH), DpiScale(STRIP_HEIGHT))
            : new Size(DpiScale(BALL_SIZE), DpiScale(BALL_SIZE));
        ClientSize = size;
        Render();
    }

    private void Render()
    {
        if (!IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        EnableLayered();
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.Transparent);
        if (_snapped)
        {
            DrawStrip(graphics);
        }
        else
        {
            DrawBall(graphics);
        }

        UpdateLayered(bitmap);
    }

    public (int Left, int Top, bool Snapped, bool InfoVisible) CaptureState() => (Left, Top, _snapped, _infoVisible);

    private void ClampToWorkingArea()
    {
        // Use a point inside the window rather than Screen.FromControl: a saved position can be off every monitor.
        var area = Screen.FromPoint(new Point(Left + (Width / 2), Top + (Height / 2))).WorkingArea;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void PositionInitialBall()
    {
        // Start near the top-left of the primary work area. A fixed anchor is safer than mixing DPI-scaled
        // window coordinates with physical screen coordinates on high-DPI monitors.
        var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        Left = area.Left + DpiScale(24);
        Top = area.Top + DpiScale(120);
    }

    private void EnsureVisible()
    {
        if (!GetWindowRect(Handle, out var rect))
        {
            return;
        }

        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
        {
            return;
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        Left = area.Left + DpiScale(24);
        Top = area.Top + DpiScale(120);
    }

    private void DrawBall(Graphics graphics)
    {
        var bounds = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 3), Math.Max(1, ClientSize.Height - 3));
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        DrawGlass(graphics, path, bounds);
        DrawLiquid(graphics, path, bounds);

        var text = SystemColors.ControlText;
        using var textBrush = new SolidBrush(text);
        using var mutedBrush = new SolidBrush(Color.FromArgb(170, text));
        using var valueFont = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        var cpu = _sample?.CpuTemperature;
        if (_infoVisible)
        {
            var rows = new List<(string Label, string Value)>();
            if (cpu is { } temperature)
            {
                rows.Add(("CPU", $"{temperature.ToString("0", CultureInfo.InvariantCulture)}°C"));
            }

            if (_sample?.MemoryLoad is { } memory)
            {
                rows.Add((_t["floating.memoryShort"], $"{memory.ToString("0", CultureInfo.InvariantCulture)}%"));
            }

            if (rows.Count > 0)
            {
                var rowHeight = ClientSize.Height / (float)(rows.Count + 1);
                for (var index = 0; index < rows.Count; index++)
                {
                    var y = (index + 1) * rowHeight;
                    var line = $"{rows[index].Label} {rows[index].Value}";
                    graphics.DrawString(line, valueFont, textBrush, new RectangleF(0, y - (rowHeight / 2), ClientSize.Width, rowHeight), centered);
                }
            }
        }

        if (_cleaning)
        {
            using var cleaningFont = new Font(Font.FontFamily, 7.5f, FontStyle.Regular);
            graphics.DrawString(_t["floating.cleaning"], cleaningFont, mutedBrush, new RectangleF(0, ClientSize.Height * 0.78f, ClientSize.Width, ClientSize.Height * 0.16f), centered);
        }
    }

    private void DrawStrip(Graphics graphics)
    {
        var bounds = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 3), Math.Max(1, ClientSize.Height - 3));
        using var path = RoundedRect(bounds, DpiScale(8));
        DrawGlass(graphics, path, bounds);
        DrawLiquid(graphics, path, bounds);
    }

    private void DrawLiquid(Graphics graphics, GraphicsPath container, Rectangle bounds)
    {
        var fraction = Math.Clamp((_sample?.MemoryLoad ?? 0) / 100d, 0d, 1d);
        if (fraction <= 0)
        {
            return;
        }

        var top = bounds.Bottom - (int)Math.Round(bounds.Height * fraction);
        var amplitude = Math.Max(1, DpiScale(2));
        var wave = new GraphicsPath();
        var points = new List<PointF> { new(bounds.Left, bounds.Bottom) };
        for (var x = bounds.Left; x <= bounds.Right; x += Math.Max(1, DpiScale(3)))
        {
            var y = top + (float)(Math.Sin((_animationPhase * 1.8) + (x / (double)DpiScale(12))) * amplitude);
            points.Add(new PointF(x, y));
        }

        points.Add(new PointF(bounds.Right, bounds.Bottom));
        wave.AddLines(points.ToArray());
        wave.CloseFigure();
        var state = graphics.Save();
        graphics.SetClip(container);
        using var liquid = new SolidBrush(Color.FromArgb(105, SystemColors.Highlight));
        graphics.FillPath(liquid, wave);
        using var shine = new Pen(Color.FromArgb(55, 255, 255, 255), 1f);
        graphics.DrawLine(shine, bounds.Left + DpiScale(4), top, bounds.Right - DpiScale(4), top);
        graphics.Restore(state);
        wave.Dispose();
    }

    private void DrawGlass(Graphics graphics, GraphicsPath path, Rectangle bounds)
    {
        var tint = Application.IsDarkModeEnabled ? Color.FromArgb(188, 26, 32, 42) : Color.FromArgb(204, 243, 247, 252);
        using var fill = new SolidBrush(tint);
        graphics.FillPath(fill, path);
        var pulse = (float)((Math.Sin(_animationPhase) + 1) / 2);
        using var ring = new Pen(Color.FromArgb((int)(42 + (pulse * 36)), SystemColors.ControlText), 1.2f);
        graphics.DrawPath(ring, path);
        using var inner = new Pen(Color.FromArgb(36, 255, 255, 255), 1f);
        graphics.DrawPath(inner, path);
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
            if (e.Button == MouseButtons.Right)
            {
                _menu.Show(this, e.Location);
            }

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

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Wait for a possible second click: a single click toggles the information, a double click cleans memory.
        if (e.Clicks >= 2)
        {
            _clickTimer.Stop();
            _clickPending = false;
            _ = CleanMemoryAsync();
            return;
        }

        if (!_clickPending)
        {
            _clickPending = true;
            _clickTimer.Start();
        }
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

        DockTo(rightDistance <= leftDistance);
    }

    private void DockTo(bool left)
    {
        var screen = Screen.FromControl(this).WorkingArea;
        _snapped = true;
        ApplyMode();
        Left = left ? screen.Left : screen.Right - Width;
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
    }

    private void Undock()
    {
        _snapped = false;
        ApplyMode();
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
        Render();
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
            Render();
        }
    }

    private int DpiScale(int logical) => (int)Math.Round(logical * DeviceDpi / 96d);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    private void EnableLayered()
    {
        var style = GetWindowLong(Handle, -20);
        SetWindowLong(Handle, -20, style | 0x80000 | 0x80);
    }

    private void UpdateLayered(Bitmap bitmap)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = SelectObject(memory, hBitmap);
        try
        {
            var destination = PointToScreen(Point.Empty);
            var source = Point.Empty;
            var size = new Size(bitmap.Width, bitmap.Height);
            var blend = new BlendFunction
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1,
            };
            UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(hBitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr window,
        IntPtr destinationDc,
        ref Point destination,
        ref Size size,
        IntPtr sourceDc,
        ref Point source,
        int colorKey,
        ref BlendFunction blend,
        int flags);
}
