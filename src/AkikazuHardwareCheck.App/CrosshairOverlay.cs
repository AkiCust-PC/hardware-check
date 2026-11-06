using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

internal enum CrosshairShape
{
    Dot = 0,
    Cross = 1,
    T = 2,
    Circle = 3,
}

internal sealed record CrosshairSettings(
    CrosshairShape Shape,
    Color Color,
    int Length,
    int Thickness,
    int Gap,
    int Opacity)
{
    public static CrosshairSettings FromSettings(AkikazuHardwareCheck.Core.Configuration.AppSettings settings) => new(
        (CrosshairShape)Math.Clamp(settings.CrosshairShape, 0, 3),
        Color.FromArgb(settings.CrosshairColorArgb),
        Math.Clamp(settings.CrosshairLength, 1, 200),
        Math.Clamp(settings.CrosshairThickness, 1, 20),
        Math.Clamp(settings.CrosshairGap, 0, 100),
        Math.Clamp(settings.CrosshairOpacity, 10, 100));
}

/// <summary>
/// A click-through screen overlay. It never reads or writes game memory and never sends input; it only draws pixels.
/// </summary>
internal sealed class CrosshairOverlayForm : Form
{
    private CrosshairSettings _settings;

    public CrosshairOverlayForm(CrosshairSettings settings)
    {
        _settings = settings;
        Text = "AkikazuHardwareCheck.Crosshair";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
        BackColor = Color.Black;
        DoubleBuffered = true;
    }

    public void Apply(CrosshairSettings settings)
    {
        _settings = settings;
        Render();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message message)
    {
        // HTTRANSPARENT passes mouse input through the overlay without making the layered window invisible.
        if (message.Msg == 0x0084)
        {
            message.Result = new IntPtr(-1);
            return;
        }

        base.WndProc(ref message);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000080 | 0x08000000 | 0x00080000;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
    }

    private void Render()
    {
        if (!IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            CrosshairRenderer.Draw(graphics, ClientSize, _settings);
        }

        UpdateLayered(bitmap);
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
            var blend = new BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
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
    private static extern int GetSystemMetrics(int index);

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

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, int colorKey, ref BlendFunction blend, int flags);
}

internal static class CrosshairRenderer
{
    public static void Draw(Graphics graphics, Size size, CrosshairSettings settings)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Max(1f, graphics.DpiX / 96f);
        var centerX = size.Width / 2f;
        var centerY = size.Height / 2f;
        var length = Math.Max(1, settings.Length) * scale;
        var gap = Math.Max(0, settings.Gap) * scale;
        var thickness = Math.Max(1, settings.Thickness) * scale;
        var opacity = Math.Clamp(settings.Opacity, 0, 100);
        var color = Color.FromArgb((int)Math.Round(255 * opacity / 100d), settings.Color.R, settings.Color.G, settings.Color.B);
        using var pen = new Pen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var brush = new SolidBrush(color);

        switch (settings.Shape)
        {
            case CrosshairShape.Dot:
                var radius = Math.Max(1f, thickness / 2f);
                graphics.FillEllipse(brush, centerX - radius, centerY - radius, radius * 2, radius * 2);
                break;
            case CrosshairShape.Cross:
                DrawCross(graphics, pen, centerX, centerY, length, gap);
                break;
            case CrosshairShape.T:
                graphics.DrawLine(pen, centerX - gap - length, centerY, centerX - gap, centerY);
                graphics.DrawLine(pen, centerX + gap, centerY, centerX + gap + length, centerY);
                graphics.DrawLine(pen, centerX, centerY + gap, centerX, centerY + gap + length);
                break;
            case CrosshairShape.Circle:
                var circleRadius = Math.Max(1f, gap + length);
                graphics.DrawEllipse(pen, centerX - circleRadius, centerY - circleRadius, circleRadius * 2, circleRadius * 2);
                break;
        }
    }

    private static void DrawCross(Graphics graphics, Pen pen, float centerX, float centerY, float length, float gap)
    {
        graphics.DrawLine(pen, centerX - gap - length, centerY, centerX - gap, centerY);
        graphics.DrawLine(pen, centerX + gap, centerY, centerX + gap + length, centerY);
        graphics.DrawLine(pen, centerX, centerY - gap - length, centerX, centerY - gap);
        graphics.DrawLine(pen, centerX, centerY + gap, centerX, centerY + gap + length);
    }
}

internal sealed class CrosshairSettingsForm : Form
{
    private readonly Translator _t;
    private readonly ComboBox _shape;
    private readonly Button _colorButton;
    private readonly NumericUpDown _length;
    private readonly NumericUpDown _thickness;
    private readonly NumericUpDown _gap;
    private readonly TrackBar _opacity;
    private readonly Label _opacityValue;
    private readonly Panel _preview;
    private Color _color;
    private CrosshairSettings? _result;

    public CrosshairSettingsForm(Translator t, CrosshairSettings settings, Font font)
    {
        _t = t;
        _color = settings.Color;
        Text = t["crosshair.settings"];
        Icon = Ui.AppIcon;
        Font = font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Ui.SPACING * 2);

        _shape = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        _shape.Items.AddRange([t["crosshair.dot"], t["crosshair.cross"], t["crosshair.t"], t["crosshair.circle"]]);
        _shape.SelectedIndex = (int)settings.Shape;

        _colorButton = new Button { Text = t["crosshair.color"], AutoSize = true, BackColor = _color, ForeColor = Contrast(_color) };
        _colorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _color, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _color = dialog.Color;
                _colorButton.BackColor = _color;
                _colorButton.ForeColor = Contrast(_color);
                UpdatePreview();
            }
        };

        _length = Number(settings.Length, 1, 200);
        _thickness = Number(settings.Thickness, 1, 20);
        _gap = Number(settings.Gap, 0, 100);
        _opacity = new TrackBar { Minimum = 10, Maximum = 100, TickFrequency = 10, Value = Math.Clamp(settings.Opacity, 10, 100), Width = 180 };
        _opacityValue = new Label { AutoSize = true, Text = $"{_opacity.Value}%", Margin = new Padding(Ui.SPACING / 2, 0, 0, 0) };
        _opacity.ValueChanged += (_, _) =>
        {
            _opacityValue.Text = $"{_opacity.Value}%";
            UpdatePreview();
        };

        _preview = new Panel { Width = 220, Height = 120, BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle };
        _preview.Paint += (_, e) => CrosshairRenderer.Draw(e.Graphics, _preview.ClientSize, CurrentSettings());

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(layout, 0, Ui.Label(), t["crosshair.shape"], _shape);
        AddRow(layout, 1, Ui.Label(), t["crosshair.color"], _colorButton);
        AddRow(layout, 2, Ui.Label(), t["crosshair.length"], _length);
        AddRow(layout, 3, Ui.Label(), t["crosshair.thickness"], _thickness);
        AddRow(layout, 4, Ui.Label(), t["crosshair.gap"], _gap);
        AddRow(layout, 5, Ui.Label(), t["crosshair.opacity"], _opacity, _opacityValue);
        layout.Controls.Add(_preview, 0, 6);
        layout.SetColumnSpan(_preview, 2);
        var hint = new Label
        {
            Text = t["crosshair.hint"],
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, Ui.SPACING, 0, 0),
        };
        layout.Controls.Add(hint, 0, 7);
        layout.SetColumnSpan(hint, 2);

        var ok = new Button { Text = _t["ui.ok"], DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = _t["ui.cancel"], DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Ui.SPACING, 0, 0) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(layout, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            _result = CurrentSettings();
            DialogResult = DialogResult.OK;
            Close();
        };
        _shape.SelectedIndexChanged += (_, _) => UpdatePreview();
        _length.ValueChanged += (_, _) => UpdatePreview();
        _thickness.ValueChanged += (_, _) => UpdatePreview();
        _gap.ValueChanged += (_, _) => UpdatePreview();
    }

    public CrosshairSettings? Result => _result;

    private static NumericUpDown Number(int value, int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = Math.Clamp(value, minimum, maximum),
        Width = 80,
    };

    private static void AddRow(TableLayoutPanel layout, int row, Label label, string text, Control control, Control? trailing = null)
    {
        label.Text = text;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
        if (trailing is not null)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            flow.Controls.Add(control);
            flow.Controls.Add(trailing);
            layout.Controls.Remove(control);
            layout.Controls.Add(flow, 1, row);
        }
    }

    private static Color Contrast(Color color) =>
        ((color.R * 299) + (color.G * 587) + (color.B * 114)) > 140000 ? Color.Black : Color.White;

    private CrosshairSettings CurrentSettings() => new(
        (CrosshairShape)_shape.SelectedIndex,
        _color,
        (int)_length.Value,
        (int)_thickness.Value,
        (int)_gap.Value,
        _opacity.Value);

    private void UpdatePreview() => _preview.Invalidate();
}
