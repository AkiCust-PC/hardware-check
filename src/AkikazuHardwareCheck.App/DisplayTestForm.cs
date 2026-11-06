using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// The full-screen monitor suite mirrored from the website: 13 patterns, wheel / arrow paging, a progress rail,
/// per-test settings, idle hiding and a response-time animation. All measurement is visual and judged by the operator.
/// </summary>
internal sealed class DisplayTestForm : Form, IMessageFilter
{
    private const int WHEEL_PAGE_INTERVAL_MS = 600;
    private const int UI_IDLE_TIMEOUT_MS = 3000;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_MOUSEMOVE = 0x0200;

    internal static readonly string[] TestIds =
    [
        "testPattern", "black", "white", "red", "green", "blue", "uniformity", "colorDistance",
        "gradient", "sharpness", "viewingAngle", "gamma", "responseTime",
    ];

    internal static readonly int[] GradientSteps = [8, 16, 32, 64, 128, 256];
    internal static readonly int[] UniformityLevels = [25, 50, 75];

    private static readonly Color[] ViewingColors =
    [
        Color.White, Color.FromArgb(191, 191, 191), Color.FromArgb(128, 128, 128), Color.FromArgb(64, 64, 64),
        Color.Red, Color.Lime, Color.Blue, Color.Cyan, Color.Magenta, Color.Yellow,
        Color.FromArgb(224, 172, 143), Color.FromArgb(255, 128, 0),
    ];

    private readonly Translator _t;
    private readonly DisplaySurface _surface;
    private readonly Panel _controls = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(235, 30, 30, 30), Padding = new Padding(Ui.SPACING) };
    private readonly FlowLayoutPanel _rail = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = 44, BackColor = Color.FromArgb(160, 0, 0, 0) };
    private readonly Label _progress = new() { AutoSize = true, ForeColor = Color.White, BackColor = Color.Transparent };
    private readonly Button _exit = new() { AutoSize = true, Text = "×" };
    private readonly System.Windows.Forms.Timer _idleTimer = new() { Interval = 250 };
    private readonly List<Button> _railButtons = [];

    private int _index;
    private readonly HashSet<int> _completed = [];
    private bool _pinnedHidden;
    private bool _uiVisible = true;
    private DateTime _lastActivity = DateTime.UtcNow;
    private long _lastWheelAt;
    private bool _building;
    private int _uniformity = 50;
    // White is the gray mask (1,1,1); red/green/blue keep their channel mask.
    private Color _colorBase = Color.White;
    private int _level = 128;
    private int _difference = 6;
    private Color _gradientColor = Color.White;
    private int _gradientStepIndex = GradientSteps.Length - 1;
    private bool _sharpnessInverted;
    private int _sharpnessSize = 12;
    private double _gamma = 2.2;
    private int _responseSpeed = 960;
    private int _responseDistance = 75;
    private Color _responseBackground = Color.FromArgb(128, 128, 128);
    private Color _responseLeft = Color.White;
    private Color _responseRight = Color.White;

    public DisplayTestForm(Translator t, Screen screen)
    {
        _t = t;
        Font = Ui.FontFor(t.Language);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Color.Black;
        DoubleBuffered = true;
        Text = t["peripheralTest.display"];

        _surface = new DisplaySurface { Dock = DockStyle.Fill };
        Controls.Add(_surface);

        _progress.Location = new Point(14, 12);
        Controls.Add(_progress);

        _exit.Text = t["displayTest.exit"];
        _exit.ForeColor = Color.White;
        _exit.BackColor = Color.FromArgb(40, 40, 40);
        _exit.FlatStyle = FlatStyle.Flat;
        _exit.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
        _exit.Click += (_, _) => Close();
        Controls.Add(_exit);

        for (var index = 0; index < TestIds.Length; index++)
        {
            var button = new Button { Text = (index + 1).ToString(CultureInfo.InvariantCulture), Width = 34, Height = 28, Margin = new Padding(0, 1, 0, 1), FlatStyle = FlatStyle.System };
            var target = index;
            button.Click += (_, _) => GoTo(target);
            _railButtons.Add(button);
            _rail.Controls.Add(button);
        }

        Controls.Add(_rail);
        Controls.Add(_controls);
        _controls.MinimumSize = new Size(LogicalToDeviceUnits(680), 0);
        _controls.MaximumSize = new Size(LogicalToDeviceUnits(700), 0);
        _progress.Font = new Font(Font.FontFamily, 14, FontStyle.Bold);
        _controls.BringToFront();
        _rail.BringToFront();
        _exit.BringToFront();
        _progress.BringToFront();

        _idleTimer.Tick += (_, _) => IdleCheck();
        Shown += (_, _) =>
        {
            Application.AddMessageFilter(this);
            _surface.Focus();
            _idleTimer.Start();
            ShowTest(0);
        };
        FormClosed += (_, _) =>
        {
            Application.RemoveMessageFilter(this);
            _idleTimer.Dispose();
            _surface.DisposeTimer();
            Cursor.Show();
        };
        _surface.Activity += RegisterActivity;
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == WM_MOUSEWHEEL)
        {
            var delta = (short)((long)m.WParam >> 16);
            var now = Environment.TickCount64;
            if (!IsOverControls() && delta != 0 && now - _lastWheelAt >= WHEEL_PAGE_INTERVAL_MS)
            {
                _lastWheelAt = now;
                TurnPage(Math.Sign(delta));
            }

            return false;
        }

        if (m.Msg == WM_MOUSEMOVE)
        {
            RegisterActivity();
        }

        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Down or Keys.Right or Keys.PageDown:
                TurnPage(1);
                return true;
            case Keys.Up or Keys.Left or Keys.PageUp:
                TurnPage(-1);
                return true;
            case Keys.Space:
                _pinnedHidden = !_pinnedHidden;
                SetUiVisible(!_pinnedHidden);
                return true;
            case Keys.Escape:
                Close();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void TurnPage(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        GoTo(_index + Math.Sign(delta));
    }

    private void GoTo(int index)
    {
        if (index >= TestIds.Length)
        {
            MarkCompleted();
            Close();
            return;
        }

        index = Math.Clamp(index, 0, TestIds.Length - 1);
        if (index == _index)
        {
            return;
        }

        MarkCompleted();
        _index = index;
        _pinnedHidden = false;
        ShowTest(index);
        RegisterActivity();
    }

    internal void ShowForSmoke(int index) => ShowTest(Math.Clamp(index, 0, TestIds.Length - 1));

    private void ShowTest(int index)
    {
        _index = index;
        _surface.SetSettings(CurrentSettings());
        BuildControls();
        UpdateRail();
        UpdateProgress();
        SetUiVisible(true);
    }

    private void MarkCompleted() => _completed.Add(_index);

    private void UpdateProgress()
    {
        _progress.Text = _t["displayTest.progress", _completed.Count, TestIds.Length];
    }

    private void UpdateRail()
    {
        for (var index = 0; index < _railButtons.Count; index++)
        {
            _railButtons[index].BackColor = index == _index ? SystemColors.Highlight : SystemColors.Control;
            _railButtons[index].ForeColor = index == _index ? SystemColors.HighlightText : SystemColors.ControlText;
        }
    }

    private DisplaySettings CurrentSettings() => new(
        _index,
        _uniformity,
        _colorBase,
        _level,
        _difference,
        _gradientColor,
        GradientSteps[_gradientStepIndex],
        _sharpnessInverted,
        _sharpnessSize,
        _gamma,
        _responseSpeed,
        _responseDistance,
        _responseBackground,
        _responseLeft,
        _responseRight,
        _t["displayTest.sharpnessSample"]);

    private void BuildControls()
    {
        _building = true;
        _controls.SuspendLayout();
        var old = _controls.Controls.Cast<Control>().ToList();
        _controls.Controls.Clear();
        foreach (var control in old)
        {
            control.Dispose();
        }

        var table = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Width = _controls.Width - (_controls.Padding.Horizontal),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var name = new Label
        {
            Text = _t[$"displayTest.{TestIds[_index]}.name"],
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0),
        };
        var hint = new Label
        {
            Text = _t[$"displayTest.{TestIds[_index]}.hint"],
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 210, 210),
            MaximumSize = new Size(LogicalToDeviceUnits(620), 0),
            Margin = new Padding(0, 2, 0, Ui.SPACING),
        };
        table.Controls.Add(name, 0, 0);
        table.Controls.Add(hint, 0, 1);

        var settings = BuildSettings();
        if (settings is not null)
        {
            table.Controls.Add(settings, 0, 2);
        }

        var footer = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, Ui.SPACING, 0, 0) };
        footer.Controls.Add(new Label
        {
            Text = _t["displayTest.shortcuts"],
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 180, 180),
            Margin = new Padding(0, 6, Ui.SPACING, 0),
        });
        var reset = Ui.Button();
        reset.Text = _t["displayTest.reset"];
        reset.Click += (_, _) =>
        {
            ResetSettings();
            BuildControls();
            _surface.SetSettings(CurrentSettings());
        };
        footer.Controls.Add(reset);
        table.Controls.Add(footer, 0, settings is null ? 2 : 3);
        _controls.Controls.Add(table);
        _controls.PerformLayout();
        _controls.ResumeLayout();
        _building = false;
        LayoutOverlay();
    }

    private Control? BuildSettings()
    {
        if (TestIds[_index] is "uniformity")
        {
            var row = Ui.Flow();
            foreach (var level in UniformityLevels)
            {
                var button = Ui.Button();
                button.Text = $"{level} %";
                button.Click += (_, _) =>
                {
                    _uniformity = level;
                    Apply();
                };
                row.Controls.Add(button);
            }

            return row;
        }

        if (TestIds[_index] is "colorDistance")
        {
            var row = Ui.Flow();
            foreach (var (name, color) in new[] { ("gray", Color.White), ("red", Color.Red), ("green", Color.Lime), ("blue", Color.Blue) })
            {
                var button = new Button { Width = 30, Height = 30, BackColor = color, FlatStyle = FlatStyle.Flat, Margin = new Padding(2), Text = "" };
                button.Click += (_, _) =>
                {
                    _colorBase = color;
                    Apply();
                };
                row.Controls.Add(button);
            }

            var brightness = Slider(0, 255, _level, value =>
            {
                _level = value;
                Apply();
            });
            var difference = Slider(1, 32, _difference, value =>
            {
                _difference = value;
                Apply();
            });
            return Stack(row, Labeled(_t["displayTest.brightness", _level], brightness), Labeled(_t["displayTest.difference", _difference], difference));
        }

        if (TestIds[_index] is "gradient")
        {
            var row = Ui.Flow();
            foreach (var (name, color) in new[]
            {
                ("grayscale", Color.White), ("cyan", Color.Cyan), ("magenta", Color.Magenta), ("yellow", Color.Yellow),
                ("red", Color.Red), ("green", Color.Lime), ("blue", Color.Blue),
            })
            {
                var button = new Button { Width = 40, Height = 28, BackColor = color, FlatStyle = FlatStyle.Flat, Margin = new Padding(2), Text = "" };
                button.Click += (_, _) =>
                {
                    _gradientColor = color;
                    Apply();
                };
                row.Controls.Add(button);
            }

            var steps = Slider(0, GradientSteps.Length - 1, _gradientStepIndex, value =>
            {
                _gradientStepIndex = value;
                Apply();
            });
            return Stack(row, Labeled(_t["displayTest.steps", GradientSteps[_gradientStepIndex]], steps));
        }

        if (TestIds[_index] is "sharpness")
        {
            var inverted = new CheckBox { Text = _t["displayTest.darkBackground"], AutoSize = true, Checked = _sharpnessInverted, ForeColor = Color.White, BackColor = Color.Transparent };
            inverted.CheckedChanged += (_, _) =>
            {
                _sharpnessInverted = inverted.Checked;
                Apply();
            };
            var size = Slider(10, 18, _sharpnessSize, value =>
            {
                _sharpnessSize = value;
                Apply();
            });
            return Stack(inverted, Labeled(_t["displayTest.fontSize", _sharpnessSize], size));
        }

        if (TestIds[_index] is "gamma")
        {
            var gamma = Slider(100, 300, (int)Math.Round(_gamma * 100), value =>
            {
                _gamma = value / 100d;
                Apply();
            });
            return Labeled(_t["displayTest.gamma", _gamma.ToString("0.00", CultureInfo.InvariantCulture)], gamma);
        }

        if (TestIds[_index] is "responseTime")
        {
            var speed = Slider(480, 3840, _responseSpeed, value =>
            {
                _responseSpeed = value;
                Apply();
            });
            speed.SmallChange = 480;
            speed.LargeChange = 480;
            speed.TickFrequency = 480;
            var distance = Slider(0, 150, _responseDistance, value =>
            {
                _responseDistance = value;
                Apply();
            });
            var swatches = BuildSwatches();
            var background = Select(swatches, _responseBackground, color =>
            {
                _responseBackground = color;
                Apply();
            });
            var left = Select(swatches, _responseLeft, color =>
            {
                _responseLeft = color;
                Apply();
            });
            var right = Select(swatches, _responseRight, color =>
            {
                _responseRight = color;
                Apply();
            });
            return Stack(
                Labeled(_t["displayTest.speed", _responseSpeed], speed),
                Labeled(_t["displayTest.distance", _responseDistance], distance),
                Labeled(_t["displayTest.background"], background),
                Labeled(_t["displayTest.leftRectangle"], left),
                Labeled(_t["displayTest.rightRectangle"], right));
        }

        return null;
    }

    private void Apply()
    {
        if (_building)
        {
            return;
        }

        _surface.SetSettings(CurrentSettings());
    }

    private void ResetSettings()
    {
        _uniformity = 50;
        _colorBase = Color.White;
        _level = 128;
        _difference = 6;
        _gradientColor = Color.White;
        _gradientStepIndex = GradientSteps.Length - 1;
        _sharpnessInverted = false;
        _sharpnessSize = 12;
        _gamma = 2.2;
        _responseSpeed = 960;
        _responseDistance = 75;
        _responseBackground = Color.FromArgb(128, 128, 128);
        _responseLeft = Color.White;
        _responseRight = Color.White;
    }

    private List<(string Label, Color Color)> BuildSwatches()
    {
        var list = new List<(string, Color)>
        {
            (_t["displayTest.colors.black"], Color.Black),
            (_t["displayTest.colors.white"], Color.White),
        };
        foreach (var percent in UniformityLevels)
        {
            var value = (int)Math.Round(255 * percent / 100d);
            list.Add((_t["displayTest.colorVariants.shade", _t["displayTest.colors.gray"], percent], Color.FromArgb(value, value, value)));
        }

        foreach (var (baseName, color) in new[]
        {
            ("cyan", Color.Cyan), ("magenta", Color.Magenta), ("yellow", Color.Yellow),
            ("red", Color.Red), ("green", Color.Lime), ("blue", Color.Blue),
        })
        {
            foreach (var percent in UniformityLevels)
            {
                list.Add((_t["displayTest.colorVariants.shade", _t[$"displayTest.colors.{baseName}"], percent], Scale(color, percent)));
            }

            list.Add((_t[$"displayTest.colors.{baseName}"], color));
            list.Add((_t["displayTest.colorVariants.bright", _t[$"displayTest.colors.{baseName}"]], Lift(color, 128)));
            list.Add((_t["displayTest.colorVariants.pastel", _t[$"displayTest.colors.{baseName}"]], Lift(color, 192)));
        }

        return list;
    }

    private ComboBox Select(List<(string Label, Color Color)> swatches, Color selected, Action<Color> changed)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(0, 0, 0, Ui.SPACING) };
        for (var index = 0; index < swatches.Count; index++)
        {
            combo.Items.Add(swatches[index].Label);
            if (swatches[index].Color == selected)
            {
                combo.SelectedIndex = index;
            }
        }

        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                changed(swatches[combo.SelectedIndex].Color);
            }
        };
        return combo;
    }

    private static Color Scale(Color color, int percent) =>
        Color.FromArgb(color.R * percent / 100, color.G * percent / 100, color.B * percent / 100);

    private static Color Lift(Color color, int floor) =>
        Color.FromArgb(Math.Max(color.R, floor), Math.Max(color.G, floor), Math.Max(color.B, floor));

    private static Control Labeled(string text, Control control)
    {
        var panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = text, AutoSize = true, ForeColor = Color.White, Margin = new Padding(0, 6, Ui.SPACING, 0) }, 0, 0);
        panel.Controls.Add(control, 1, 0);
        return panel;
    }

    private static Control Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static TrackBar Slider(int minimum, int maximum, int value, Action<int> changed)
    {
        var track = new TrackBar { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(value, minimum, maximum), Width = 240, TickStyle = TickStyle.None, Margin = new Padding(0, 0, 0, Ui.SPACING) };
        track.ValueChanged += (_, _) => changed(track.Value);
        return track;
    }

    private void LayoutOverlay()
    {
        _exit.Location = new Point(ClientSize.Width - _exit.Width - 12, 12);
        _progress.Location = new Point(14, 12);
        _controls.Location = new Point(
            Math.Max(12, (ClientSize.Width - _controls.Width) / 2),
            Math.Max(12, ClientSize.Height - _controls.Height - 20));
        _rail.Location = new Point(8, Math.Max(12, (ClientSize.Height - _rail.Height) / 2));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutOverlay();
    }

    private bool IsOverControls()
    {
        var point = PointToClient(Cursor.Position);
        return _controls.Bounds.Contains(point) || _rail.Bounds.Contains(point) || _exit.Bounds.Contains(point);
    }

    private void RegisterActivity()
    {
        _lastActivity = DateTime.UtcNow;
        if (!_pinnedHidden && !_uiVisible)
        {
            SetUiVisible(true);
        }
    }

    private void IdleCheck()
    {
        if (!_pinnedHidden && _uiVisible && DateTime.UtcNow - _lastActivity >= TimeSpan.FromMilliseconds(UI_IDLE_TIMEOUT_MS))
        {
            SetUiVisible(false);
        }
    }

    private void SetUiVisible(bool visible)
    {
        _uiVisible = visible;
        _controls.Visible = visible;
        _rail.Visible = visible;
        _exit.Visible = visible;
        _progress.Visible = visible;
        if (visible)
        {
            Cursor.Show();
        }
        else
        {
            Cursor.Hide();
        }
    }

    internal sealed record DisplaySettings(
        int Index,
        int Uniformity,
        Color ColorBase,
        int Level,
        int Difference,
        Color GradientColor,
        int GradientSteps,
        bool SharpnessInverted,
        int SharpnessSize,
        double Gamma,
        int ResponseSpeed,
        int ResponseDistance,
        Color ResponseBackground,
        Color ResponseLeft,
        Color ResponseRight,
        string SharpnessSample);

    private sealed class DisplaySurface : Control
    {
        private readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private float _offset;

        public DisplaySurface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            _animation.Tick += (_, _) =>
            {
                if (Settings?.Index == 12)
                {
                    UpdateOffset();
                    Invalidate();
                }
            };
        }

        public DisplaySettings? Settings;

        public event Action? Activity;

        public void SetSettings(DisplaySettings? settings)
        {
            Settings = settings;
            _clock.Restart();
            _offset = 0;
            _animation.Enabled = settings?.Index == 12;
            Invalidate();
        }

        public void DisposeTimer() => _animation.Dispose();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Settings is not { } settings)
            {
                return;
            }

            var graphics = e.Graphics;
            graphics.Clear(Color.Black);
            switch (settings.Index)
            {
                case 0:
                    DrawTestPattern(graphics, ClientSize);
                    break;
                case 1:
                    graphics.Clear(Color.Black);
                    break;
                case 2:
                    graphics.Clear(Color.White);
                    break;
                case 3:
                    graphics.Clear(Color.Red);
                    break;
                case 4:
                    graphics.Clear(Color.Lime);
                    break;
                case 5:
                    graphics.Clear(Color.Blue);
                    break;
                case 6:
                    var gray = (int)Math.Round(255 * settings.Uniformity / 100d);
                    graphics.Clear(Color.FromArgb(gray, gray, gray));
                    break;
                case 7:
                    DrawColorDistance(graphics, ClientSize, settings);
                    break;
                case 8:
                    DrawGradient(graphics, ClientSize, settings.GradientColor, settings.GradientSteps);
                    break;
                case 9:
                    DrawSharpness(graphics, ClientSize, settings);
                    break;
                case 10:
                    DrawViewingAngle(graphics, ClientSize);
                    break;
                case 11:
                    DrawGamma(graphics, ClientSize, settings.Gamma);
                    break;
                case 12:
                    DrawResponse(graphics, ClientSize, settings);
                    break;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Activity?.Invoke();
            base.OnMouseMove(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _animation.Enabled = Settings?.Index == 12;
            _offset = 0;
            Invalidate();
        }

        private void UpdateOffset()
        {
            if (Settings is not { } settings)
            {
                return;
            }

            var scale = DeviceDpi / 96f;
            var rect = 120 * scale;
            var span = rect * 2 + (settings.ResponseDistance * scale);
            var travel = ClientSize.Width + span;
            _offset = (float)((_clock.Elapsed.TotalSeconds * settings.ResponseSpeed * scale) % travel) - (float)span;
        }
    }

    private static void DrawColorDistance(Graphics graphics, Size size, DisplaySettings settings)
    {
        var level = settings.Level;
        var rectangleLevel = level + settings.Difference <= 255 ? level + settings.Difference : level - settings.Difference;
        var baseColor = settings.ColorBase;
        var background = Color.FromArgb(baseColor.R * level / 255, baseColor.G * level / 255, baseColor.B * level / 255);
        var rectangle = Color.FromArgb(baseColor.R * rectangleLevel / 255, baseColor.G * rectangleLevel / 255, baseColor.B * rectangleLevel / 255);
        graphics.Clear(background);
        using var brush = new SolidBrush(rectangle);
        var width = Math.Max(1, (int)(size.Width * 0.3));
        var height = Math.Max(1, (int)(size.Height * 0.3));
        graphics.FillRectangle(brush, (size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }

    private static void DrawGradient(Graphics graphics, Size size, Color target, int steps)
    {
        for (var step = 0; step < steps; step++)
        {
            var left = (int)Math.Round((double)step * size.Width / steps);
            var right = (int)Math.Round((double)(step + 1) * size.Width / steps);
            var ratio = steps <= 1 ? 1 : (double)step / (steps - 1);
            using var brush = new SolidBrush(Color.FromArgb(
                (int)Math.Round(target.R * ratio),
                (int)Math.Round(target.G * ratio),
                (int)Math.Round(target.B * ratio)));
            graphics.FillRectangle(brush, left, 0, Math.Max(1, right - left), size.Height);
        }
    }

    private static void DrawGamma(Graphics graphics, Size size, double gamma)
    {
        graphics.Clear(Color.Black);
        using var white = new SolidBrush(Color.White);
        for (var y = 0; y < size.Height; y += 2)
        {
            graphics.FillRectangle(white, 0, y, size.Width, 1);
        }

        var gray = (int)Math.Round(255 * Math.Pow(0.5, 1 / gamma));
        var diameter = (int)(Math.Min(size.Width, size.Height) * 0.3);
        using var circle = new SolidBrush(Color.FromArgb(gray, gray, gray));
        graphics.FillEllipse(circle, (size.Width - diameter) / 2f, (size.Height - diameter) / 2f, diameter, diameter);
    }

    private static void DrawViewingAngle(Graphics graphics, Size size)
    {
        graphics.Clear(Color.Black);
        var scale = Math.Min(size.Width / 1600f, size.Height / 900f);
        var offsetX = (size.Width - 1600 * scale) / 2;
        var offsetY = (size.Height - 900 * scale) / 2;
        for (var index = 0; index < ViewingColors.Length; index++)
        {
            var centerX = offsetX + (200 + (index % 4) * 400) * scale;
            var centerY = offsetY + (150 + (index / 4) * 300) * scale;
            var radius = 120 * scale;
            using var fill = new SolidBrush(ViewingColors[index]);
            graphics.FillEllipse(fill, centerX - radius, centerY - radius, radius * 2, radius * 2);
            using var pen = new Pen(Color.White, Math.Max(1, 2 * scale));
            graphics.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2, radius * 2);
        }
    }

    private static void DrawSharpness(Graphics graphics, Size size, DisplaySettings settings)
    {
        graphics.Clear(settings.SharpnessInverted ? Color.Black : Color.White);
        var color = settings.SharpnessInverted ? Color.White : Color.Black;
        using var font = new Font("Segoe UI", settings.SharpnessSize, FontStyle.Regular, GraphicsUnit.Point);
        var sample = settings.SharpnessSample;
        var text = string.Join(' ', Enumerable.Repeat(sample, 24));
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(8, 8, Math.Max(1, size.Width - 16), Math.Max(1, size.Height - 16)),
            color,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
    }

    private static void DrawResponse(Graphics graphics, Size size, DisplaySettings settings)
    {
        graphics.Clear(settings.ResponseBackground);
        const float rect = 120;
        var y = size.Height / 3f - rect / 2;
        var span = (rect * 2) + settings.ResponseDistance;
        var offset = (float)((Environment.TickCount64 * settings.ResponseSpeed / 1000d) % (size.Width + span)) - span;
        using var left = new SolidBrush(settings.ResponseLeft);
        using var right = new SolidBrush(settings.ResponseRight);
        graphics.FillRectangle(left, offset, y, rect, rect);
        graphics.FillRectangle(right, offset + rect + settings.ResponseDistance, y, rect, rect);
    }

    private static void DrawTestPattern(Graphics graphics, Size size)
    {
        var unit = Math.Min(size.Width, size.Height);
        var grid = Math.Max(1, (int)Math.Round(unit / 12d));
        graphics.Clear(Color.FromArgb(64, 64, 64));
        using var gridPen = new Pen(Color.FromArgb(128, 128, 128));
        for (var x = 0; x < size.Width; x += grid)
        {
            graphics.DrawLine(gridPen, x, 0, x, size.Height);
        }

        for (var y = 0; y < size.Height; y += grid)
        {
            graphics.DrawLine(gridPen, 0, y, size.Width, y);
        }

        using var white = new Pen(Color.White, Math.Max(1, unit / 400f));
        graphics.DrawRectangle(white, 0, 0, size.Width - 1, size.Height - 1);
        var centerX = size.Width / 2f;
        var centerY = size.Height / 2f;
        graphics.DrawEllipse(white, centerX - unit * 0.45f, centerY - unit * 0.45f, unit * 0.9f, unit * 0.9f);
        var corner = unit * 0.12f;
        foreach (var (x, y) in new[] { (corner, corner), (size.Width - corner, corner), (corner, size.Height - corner), (size.Width - corner, size.Height - corner) })
        {
            graphics.DrawEllipse(white, x - corner * 0.9f, y - corner * 0.9f, corner * 1.8f, corner * 1.8f);
        }

        var block = Math.Max(1, (int)Math.Round(unit / 10d));
        var left = (int)Math.Round(size.Width / 2d - block * 2);
        var top = (int)Math.Round(size.Height / 2d - block);
        for (var index = 0; index < 4; index++)
        {
            DrawFrequency(graphics, left + index * block, top, block, index + 1, vertical: true);
            DrawFrequency(graphics, left + index * block, top + block, block, index + 1, vertical: false);
        }

        var barHeight = Math.Max(1, (int)Math.Round(unit / 30d));
        var barWidth = (int)Math.Round(size.Width * 0.5);
        var barLeft = (size.Width - barWidth) / 2;
        var colors = new[] { Color.Red, Color.Lime, Color.Blue, Color.Cyan, Color.Magenta, Color.Yellow };
        for (var index = 0; index < colors.Length; index++)
        {
            var topBar = (int)Math.Round(size.Height * 0.12) + index * barHeight;
            for (var step = 0; step < 256; step++)
            {
                var segment = (int)Math.Round((double)step * barWidth / 256);
                var next = (int)Math.Round((double)(step + 1) * barWidth / 256);
                using var brush = new SolidBrush(Color.FromArgb(
                    colors[index].R * step / 255,
                    colors[index].G * step / 255,
                    colors[index].B * step / 255));
                graphics.FillRectangle(brush, barLeft + segment, topBar, Math.Max(1, next - segment), barHeight);
            }
        }

        var grayTop = (int)Math.Round(size.Height * 0.78);
        DrawGrayBar(graphics, barLeft, grayTop, barWidth, barHeight, 256);
        DrawGrayBar(graphics, barLeft, grayTop + barHeight, barWidth, barHeight, 16);
    }

    private static void DrawGrayBar(Graphics graphics, int left, int top, int width, int height, int steps)
    {
        for (var step = 0; step < steps; step++)
        {
            var segment = (int)Math.Round((double)step * width / steps);
            var next = (int)Math.Round((double)(step + 1) * width / steps);
            var value = (int)Math.Round(255d * step / Math.Max(1, steps - 1));
            using var brush = new SolidBrush(Color.FromArgb(value, value, value));
            graphics.FillRectangle(brush, left + segment, top, Math.Max(1, next - segment), height);
        }
    }

    private static void DrawFrequency(Graphics graphics, int x, int y, int size, int lineWidth, bool vertical)
    {
        using var black = new SolidBrush(Color.Black);
        using var white = new SolidBrush(Color.White);
        graphics.FillRectangle(black, x, y, size, size);
        for (var offset = 0; offset < size; offset += lineWidth * 2)
        {
            if (vertical)
            {
                graphics.FillRectangle(white, x + offset, y, lineWidth, size);
            }
            else
            {
                graphics.FillRectangle(white, x, y + offset, size, lineWidth);
            }
        }
    }
}
