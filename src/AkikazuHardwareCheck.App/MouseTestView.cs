using System.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Mouse and touchpad test: every button, both wheel axes, pinch zoom, double-click chatter, polling estimate and an
/// optional movement trail. The trail is drawn because Windows has no native control that records a pointer path.
/// </summary>
internal sealed class MouseTestView : UserControl
{
    private const int CHATTER_THRESHOLD_MS = 80;
    private const int POLLING_WINDOW_MS = 500;
    private const int TRAIL_WIDTH = 2;
    private const uint WM_MOUSEHWHEEL = 0x020E;

    private readonly Translator _t;
    private readonly Dictionary<string, Label> _counters = [];
    private readonly Dictionary<string, Label> _buttonLabels = [];
    private readonly Dictionary<MouseButtons, long> _lastDown = [];
    private readonly Label _chatter = Ui.Label();
    private readonly Label _polling = Ui.Label();
    private readonly MouseTrail _trail = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<string> _pressedNames = [];
    private readonly Dictionary<string, int> _counts = new()
    {
        ["left"] = 0, ["middle"] = 0, ["right"] = 0, ["back"] = 0, ["forward"] = 0,
        ["wheelUp"] = 0, ["wheelDown"] = 0, ["wheelLeft"] = 0, ["wheelRight"] = 0, ["zoomIn"] = 0, ["zoomOut"] = 0,
    };
    private long? _pollingStartedAt;
    private int _pollingEvents;

    public MouseTestView(Translator t, Font font)
    {
        _t = t;
        Font = font;
        Dock = DockStyle.Fill;
        Padding = new Padding(Ui.SPACING);

        var showTrail = new CheckBox { Text = t["mouseTest.trail"], AutoSize = true, Margin = new Padding(0, 0, Ui.SPACING, 0) };
        showTrail.CheckedChanged += (_, _) =>
        {
            _trail.ShowTrail = showTrail.Checked;
            _trail.ClearTrail();
        };
        var reset = Ui.Button();
        reset.Text = t["mouseTest.reset"];
        reset.Click += (_, _) => Reset();
        var top = Ui.Flow();
        top.Controls.AddRange([showTrail, reset]);

        var area = new Panel { Dock = DockStyle.Fill, MinimumSize = new Size(0, LogicalToDeviceUnits(220)), BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window };
        _trail.Hint = t["mouseTest.area"];
        _trail.Parent = area;
        area.Controls.Add(_trail);

        _trail.MouseDown += (_, e) => TrackDown(e.Button);
        _trail.MouseUp += (_, e) => TrackUp(e.Button);
        _trail.MouseWheel += (_, e) => TrackWheel(e.Delta, Control.ModifierKeys.HasFlag(Keys.Control));
        _trail.HorizontalWheel += delta => TrackHorizontalWheel(delta);
        _trail.MouseMove += (_, _) => TrackMove();
        _trail.MouseLeave += (_, _) => ClearPressed();

        var counters = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
        counters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        counters.Controls.Add(BuildCounterRow(
            ["left", "middle", "right", "back", "forward"],
            [t["mouseTest.left"], t["mouseTest.middle"], t["mouseTest.right"], t["mouseTest.back"], t["mouseTest.forward"]]), 0, 0);
        counters.Controls.Add(BuildCounterRow(
            ["wheelUp", "wheelDown", "wheelLeft", "wheelRight", "zoomIn", "zoomOut"],
            [t["mouseTest.wheelUp"], t["mouseTest.wheelDown"], t["mouseTest.wheelLeft"], t["mouseTest.wheelRight"], t["mouseTest.zoomIn"], t["mouseTest.zoomOut"]]), 0, 1);

        var stats = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, Ui.SPACING, 0, 0) };
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        stats.Controls.Add(Stat(t["mouseTest.chatter"], _chatter), 0, 0);
        stats.Controls.Add(Stat(t["mouseTest.polling"], _polling), 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hint = new Label
        {
            Text = t["mouseTest.hint"],
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(760), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, Ui.SPACING),
        };
        root.Controls.Add(hint, 0, 0);
        root.Controls.Add(top, 0, 1);
        root.Controls.Add(area, 0, 2);
        root.Controls.Add(counters, 0, 3);
        root.Controls.Add(stats, 0, 4);
        Controls.Add(root);
        UpdateCounters();
        UpdateStats();
    }

    private Control BuildCounterRow(string[] keys, string[] titles)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = keys.Length, Margin = new Padding(0) };
        for (var index = 0; index < keys.Length; index++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / keys.Length));
            var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(2), CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            var title = new Label { Text = titles[index], AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, Font.Size - 1) };
            var value = new Label { Text = "0", AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, Font.Size + 4, FontStyle.Bold) };
            panel.Controls.Add(title, 0, 0);
            panel.Controls.Add(value, 0, 1);
            _counters[keys[index]] = value;
            _buttonLabels[keys[index]] = value;
            row.Controls.Add(panel, index, 0);
        }

        return row;
    }

    private static Control Stat(string title, Label value)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            RowCount = 2,
            ColumnCount = 1,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            Margin = new Padding(2),
        };
        panel.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 0);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private void TrackDown(MouseButtons button)
    {
        var name = button switch
        {
            MouseButtons.Left => "left",
            MouseButtons.Middle => "middle",
            MouseButtons.Right => "right",
            MouseButtons.XButton1 => "back",
            MouseButtons.XButton2 => "forward",
            _ => null,
        };
        if (name is null)
        {
            return;
        }

        var now = _clock.ElapsedMilliseconds;
        if (_lastDown.TryGetValue(button, out var previous) && now - previous < CHATTER_THRESHOLD_MS)
        {
            _chatterCount++;
        }

        _lastDown[button] = now;
        _counts[name]++;
        _pressedNames.Add(name);
        UpdateCounters();
        UpdateStats();
    }

    private void TrackUp(MouseButtons button)
    {
        var name = button switch
        {
            MouseButtons.Left => "left",
            MouseButtons.Middle => "middle",
            MouseButtons.Right => "right",
            MouseButtons.XButton1 => "back",
            MouseButtons.XButton2 => "forward",
            _ => null,
        };
        if (name is not null)
        {
            _pressedNames.Remove(name);
        }

        UpdateCounters();
    }

    private void TrackWheel(int delta, bool control)
    {
        if (delta == 0)
        {
            return;
        }

        var name = control
            ? delta > 0 ? "zoomIn" : "zoomOut"
            : delta > 0 ? "wheelUp" : "wheelDown";
        _counts[name]++;
        UpdateCounters();
    }

    private void TrackHorizontalWheel(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _counts[delta > 0 ? "wheelRight" : "wheelLeft"]++;
        UpdateCounters();
    }

    private void TrackMove()
    {
        _pollingEvents++;
        _pollingStartedAt ??= _clock.ElapsedMilliseconds;
        var elapsed = _clock.ElapsedMilliseconds - _pollingStartedAt.Value;
        if (elapsed >= POLLING_WINDOW_MS)
        {
            var rate = (int)Math.Round(_pollingEvents * 1000d / elapsed);
            _pollingRate = Math.Max(_pollingRate, rate);
            _pollingEvents = 0;
            _pollingStartedAt = _clock.ElapsedMilliseconds;
            UpdateStats();
        }
    }

    private void ClearPressed()
    {
        _pressedNames.Clear();
        UpdateCounters();
    }

    private void Reset()
    {
        foreach (var name in _counts.Keys.ToList())
        {
            _counts[name] = 0;
        }

        _lastDown.Clear();
        _pressedNames.Clear();
        _chatterCount = 0;
        _pollingRate = 0;
        _pollingEvents = 0;
        _pollingStartedAt = null;
        _trail.ClearTrail();
        UpdateCounters();
        UpdateStats();
    }

    private void UpdateCounters()
    {
        foreach (var (name, label) in _counters)
        {
            label.Text = _counts[name].ToString(System.Globalization.CultureInfo.InvariantCulture);
            var isPressed = _pressedNames.Contains(name);
            label.Parent!.BackColor = isPressed ? SystemColors.Highlight : SystemColors.Control;
            label.ForeColor = isPressed ? SystemColors.HighlightText : SystemColors.ControlText;
        }
    }

    private void UpdateStats()
    {
        _chatter.Text = _chatterCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _chatter.ForeColor = _chatterCount > 0 ? Color.Firebrick : SystemColors.ControlText;
        _polling.Text = _pollingRate > 0 ? $"≈ {_pollingRate.ToString(System.Globalization.CultureInfo.InvariantCulture)} Hz" : "—";
    }

    private int _chatterCount;
    private int _pollingRate;

    private sealed class MouseTrail : Panel
    {
        private readonly List<PointF> _points = [];

        public bool ShowTrail;

        public string Hint = "";

        public event Action<int>? HorizontalWheel;

        public MouseTrail()
        {
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        public void ClearTrail()
        {
            _points.Clear();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(
                e.Graphics,
                Hint,
                Font,
                ClientRectangle,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (!ShowTrail || _points.Count < 2)
            {
                return;
            }

            using var pen = new Pen(Color.FromArgb(150, SystemColors.Highlight), TRAIL_WIDTH) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round, StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, _points.ToArray());
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (ShowTrail)
            {
                _points.Add(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (ShowTrail)
            {
                _points.Clear();
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _points.Clear();
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEHWHEEL)
            {
                HorizontalWheel?.Invoke((short)((long)m.WParam >> 16));
                return;
            }

            base.WndProc(ref m);
        }
    }
}
