using System.Drawing.Drawing2D;
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
    private const int STRIP_WIDTH = 204;
    private const int STRIP_HEIGHT = 268;
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
    private bool _dragging;
    private bool _dragMoved;
    private bool _snapped = true;
    private bool _infoVisible = true;
    private bool _cleaning;
    private bool _clickPending;
    private double _animationPhase;
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
        BackColor = SystemColors.Window;
        DoubleBuffered = true;
        ClientSize = new Size(BALL_SIZE, BALL_SIZE);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        BuildMenu();
        _sampleTimer.Tick += (_, _) => RefreshSample();
        _animationTimer.Tick += (_, _) =>
        {
            _animationPhase += 0.16;
            Invalidate();
        };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            _clickPending = false;
            _infoVisible = !_infoVisible;
            Invalidate();
        };
        Shown += (_, _) =>
        {
            ApplyMode();
            SnapToNearestEdge(force: true);
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
        Glass.Enable(Handle);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The desktop is allowed to show through the glass; the shape is drawn in OnPaint.
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

        Invalidate();
    }

    private void ApplyMode()
    {
        var size = _snapped
            ? new Size(DpiScale(STRIP_WIDTH), DpiScale(STRIP_HEIGHT))
            : new Size(DpiScale(BALL_SIZE), DpiScale(BALL_SIZE));
        ClientSize = size;
        Opacity = _snapped ? 0.94 : 0.82;
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
        DrawGlass(graphics, path, bounds);

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
        using var path = RoundedRect(bounds, DpiScale(11));
        DrawGlass(graphics, path, bounds);

        var text = SystemColors.ControlText;
        using var titleFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
        using var labelFont = new Font(Font.FontFamily, 7.5f, FontStyle.Regular);
        using var valueFont = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        using var valueBrush = new SolidBrush(text);
        using var mutedBrush = new SolidBrush(Color.FromArgb(170, text));
        using var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        var padding = DpiScale(12);
        if (_infoVisible)
        {
            graphics.DrawString(_t["floating.title"], titleFont, valueBrush, new RectangleF(padding, DpiScale(10), ClientSize.Width - (padding * 2), DpiScale(20)));
            var y = DpiScale(44);
            if (_sample?.CpuTemperature is { } cpu)
            {
                DrawRow(graphics, "CPU", $"{cpu.ToString("0", CultureInfo.InvariantCulture)}°C", labelFont, valueFont, valueBrush, mutedBrush, left, right, padding, ref y);
            }

            if (_sample?.MemoryLoad is { } memory)
            {
                DrawRow(graphics, _t["floating.memoryShort"], $"{memory.ToString("0", CultureInfo.InvariantCulture)}%", labelFont, valueFont, valueBrush, mutedBrush, left, right, padding, ref y);
            }
        }

        using var hintFont = new Font(Font.FontFamily, 7.5f, FontStyle.Regular);
        graphics.DrawString(_t["floating.clickHint"], hintFont, mutedBrush, new RectangleF(padding, ClientSize.Height - DpiScale(25), ClientSize.Width - (padding * 2), DpiScale(18)), new StringFormat { Alignment = StringAlignment.Center });
        if (_status is not null)
        {
            graphics.DrawString(_status, hintFont, valueBrush, new RectangleF(padding, ClientSize.Height - DpiScale(45), ClientSize.Width - (padding * 2), DpiScale(18)), new StringFormat { Alignment = StringAlignment.Center });
        }
    }

    private void DrawGlass(Graphics graphics, GraphicsPath path, Rectangle bounds)
    {
        var tint = Application.IsDarkModeEnabled ? Color.FromArgb(86, 18, 22, 28) : Color.FromArgb(100, 255, 255, 255);
        using var fill = new SolidBrush(tint);
        graphics.FillPath(fill, path);
        var pulse = (float)((Math.Sin(_animationPhase) + 1) / 2);
        using var ring = new Pen(Color.FromArgb((int)(42 + (pulse * 36)), SystemColors.ControlText), 1.2f);
        graphics.DrawPath(ring, path);
        using var inner = new Pen(Color.FromArgb(36, 255, 255, 255), 1f);
        graphics.DrawPath(inner, path);
    }

    private void DrawRow(Graphics graphics, string label, string value, Font labelFont, Font valueFont, Brush valueBrush, Brush mutedBrush, StringFormat left, StringFormat right, int padding, ref int y)
    {
        var rowHeight = DpiScale(27);
        graphics.DrawString(label, labelFont, mutedBrush, new RectangleF(padding, y, ClientSize.Width - (padding * 2), rowHeight), left);
        graphics.DrawString(value, valueFont, valueBrush, new RectangleF(padding, y, ClientSize.Width - (padding * 2), rowHeight), right);
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

    private static class Glass
    {
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_TRANSIENTWINDOW = 3;
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        public static void Enable(IntPtr handle)
        {
            var backdrop = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(handle, ref margins);

            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = 0x01000000,
            };
            var data = new CompositionData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
                Size = Marshal.SizeOf<AccentPolicy>(),
            };
            try
            {
                Marshal.StructureToPtr(accent, data.Data, false);
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(data.Data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionData
        {
            public int Attribute;
            public IntPtr Data;
            public int Size;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);
    }
}
