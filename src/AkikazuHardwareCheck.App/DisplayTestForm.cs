using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Full-screen display suite. It deliberately uses a PictureBox and generated bitmaps: Windows has no native
/// calibration-pattern control, and the operator judges every pattern by eye.
/// </summary>
internal sealed class DisplayTestForm : Form
{
    private const int CHECKER_SIZE = 64;
    private const int RESPONSE_BLOCK = 120;
    private const int RESPONSE_GAP = 75;
    private const int RESPONSE_SPEED = 960;

    private readonly Translator _t;
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage };
    private readonly Label _hint = new() { Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Black, ForeColor = Color.White, Height = 32 };
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 40 };
    private int _index;
    private double _responseOffset;
    private int _uniformityLevel = 50;
    private double _gamma = 2.2;
    private float _sharpness = 12f;
    private int _colorDistance = 6;
    private int _responseSpeed = RESPONSE_SPEED;
    private int _responseGap = RESPONSE_GAP;

    private static readonly Color[] SolidColors = [Color.Black, Color.White, Color.Red, Color.Lime, Color.Blue];

    public DisplayTestForm(Translator t, Screen screen)
    {
        _t = t;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Color.Black;
        _hint.Text = t["peripheralTest.displayHint"];
        Controls.Add(_picture);
        Controls.Add(_hint);
        KeyDown += OnKeyDown;
        _picture.MouseClick += (_, _) => Next();
        _animation.Tick += (_, _) =>
        {
            _responseOffset += _responseSpeed * _animation.Interval / 1000d;
            if (_responseOffset > ClientSize.Width + (RESPONSE_BLOCK * 2) + RESPONSE_GAP)
            {
                _responseOffset = -(RESPONSE_BLOCK * 2) - RESPONSE_GAP;
            }

            Render();
        };
        Shown += (_, _) => ShowPattern(0);
        Resize += (_, _) => Render();
        FormClosed += (_, _) => _animation.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;
            case Keys.Right:
            case Keys.Space:
                Next();
                break;
            case Keys.Left:
                Previous();
                break;
            case Keys.U when _index == 6:
                _uniformityLevel = _uniformityLevel switch { 25 => 50, 50 => 75, _ => 25 };
                Render();
                break;
            case Keys.Up:
                AdjustPattern(1);
                break;
            case Keys.Down:
                AdjustPattern(-1);
                break;
        }
    }

    private void AdjustPattern(int direction)
    {
        switch (_index)
        {
            case 6:
                _uniformityLevel = Math.Clamp(_uniformityLevel + (direction * 25), 25, 75);
                break;
            case 7:
                _colorDistance = Math.Clamp(_colorDistance + direction, 1, 32);
                break;
            case 9:
                _sharpness = Math.Clamp(_sharpness + direction, 10, 18);
                break;
            case 11:
                _gamma = Math.Clamp(_gamma + (direction * 0.05), 1, 3);
                break;
            case 12:
                _responseSpeed = Math.Clamp(_responseSpeed + (direction * 480), 480, 3840);
                break;
            case 0:
                return;
            default:
                return;
        }

        UpdateHint();
        Render();
    }

    private void Next() => ShowPattern((_index + 1) % 13);

    private void Previous() => ShowPattern((_index + 12) % 13);

    private void ShowPattern(int index)
    {
        _index = index;
        _animation.Enabled = index == 12;
        if (index == 12)
        {
            _responseOffset = -(RESPONSE_BLOCK * 2) - RESPONSE_GAP;
        }

        UpdateHint();
        Render();
    }

    private void UpdateHint()
    {
        var detail = _index switch
        {
            6 => $"  {_uniformityLevel.ToString(CultureInfo.InvariantCulture)}%",
            7 => $"  Δ{_colorDistance.ToString(CultureInfo.InvariantCulture)}",
            9 => $"  {_sharpness.ToString("0", CultureInfo.InvariantCulture)}pt",
            11 => $"  γ{_gamma.ToString("0.00", CultureInfo.InvariantCulture)}",
            12 => $"  {_responseSpeed.ToString(CultureInfo.InvariantCulture)}px/s",
            _ => string.Empty,
        };
        _hint.Text = $"{_t["peripheralTest.displayHint"]}  {_index + 1}/13{detail}";
    }

    private void Render()
    {
        var old = _picture.Image;
        if (_index is >= 1 and <= 5)
        {
            _picture.Image = null;
            _picture.BackColor = SolidColors[_index - 1];
            old?.Dispose();
            return;
        }

        _picture.BackColor = Color.Black;
        var size = _picture.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var bitmap = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            switch (_index)
            {
                case 0:
                    DrawTestPattern(graphics, size);
                    break;
                case 6:
                    graphics.Clear(Color.FromArgb(_uniformityLevel * 255 / 100, _uniformityLevel * 255 / 100, _uniformityLevel * 255 / 100));
                    break;
                case 7:
                    DrawColorDistance(graphics, size);
                    break;
                case 8:
                    using (var gradient = new LinearGradientBrush(new Rectangle(0, 0, size.Width, size.Height), Color.Black, Color.White, LinearGradientMode.Horizontal))
                    {
                        graphics.FillRectangle(gradient, 0, 0, size.Width, size.Height);
                    }

                    break;
                case 9:
                    DrawSharpness(graphics, size);
                    break;
                case 10:
                    DrawViewingAngle(graphics, size);
                    break;
                case 11:
                    DrawGamma(graphics, size);
                    break;
                case 12:
                    DrawResponse(graphics, size);
                    break;
            }
        }

        _picture.Image = bitmap;
        old?.Dispose();
    }

    private static void DrawTestPattern(Graphics graphics, Size size)
    {
        graphics.Clear(Color.Black);
        for (var y = 0; y < size.Height; y += CHECKER_SIZE)
        {
            for (var x = 0; x < size.Width; x += CHECKER_SIZE)
            {
                if (((x / CHECKER_SIZE) + (y / CHECKER_SIZE)) % 2 == 0)
                {
                    using var brush = new SolidBrush(Color.White);
                    graphics.FillRectangle(brush, x, y, CHECKER_SIZE, CHECKER_SIZE);
                }
            }
        }

        using var center = new Pen(Color.Red, Math.Max(2, size.Width / 500f));
        graphics.DrawLine(center, size.Width / 2f, 0, size.Width / 2f, size.Height);
        graphics.DrawLine(center, 0, size.Height / 2f, size.Width, size.Height / 2f);
        using var ring = new Pen(Color.Red, Math.Max(1, size.Width / 700f));
        var radius = Math.Min(size.Width, size.Height) * 0.12f;
        graphics.DrawEllipse(ring, size.Width / 2f - radius, size.Height / 2f - radius, radius * 2, radius * 2);
    }

    private void DrawColorDistance(Graphics graphics, Size size)
    {
        graphics.Clear(Color.FromArgb(128, 128, 128));
        var level = Math.Clamp(128 + _colorDistance, 0, 255);
        using var brush = new SolidBrush(Color.FromArgb(level, level, level));
        var width = Math.Max(80, size.Width * 3 / 10);
        var height = Math.Max(80, size.Height * 3 / 10);
        graphics.FillRectangle(brush, (size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }

    private void DrawSharpness(Graphics graphics, Size size)
    {
        graphics.Clear(Color.White);
        using var font = new Font("Segoe UI", _sharpness, FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(Color.Black);
        const string sample = "The quick brown fox jumps over the lazy dog. 0123456789 汉 字 カナ";
        var text = string.Join(' ', Enumerable.Repeat(sample, Math.Max(1, size.Width * size.Height / (sample.Length * 900))));
        graphics.DrawString(text, font, brush, new RectangleF(8, 8, size.Width - 16, size.Height - 16), new StringFormat { FormatFlags = StringFormatFlags.NoClip });
    }

    private static void DrawViewingAngle(Graphics graphics, Size size)
    {
        graphics.Clear(Color.Black);
        string[] colors = ["#FFFFFF", "#BFBFBF", "#808080", "#404040", "#FF0000", "#00FF00", "#0000FF", "#00FFFF", "#FF00FF", "#FFFF00", "#E0AC8F", "#FF8000"];
        for (var index = 0; index < colors.Length; index++)
        {
            using var fill = new SolidBrush(ColorTranslator.FromHtml(colors[index]));
            var cx = size.Width * (0.125f + ((index % 4) * 0.25f));
            var cy = size.Height * (0.16f + ((index / 4) * 0.34f));
            var radius = Math.Min(size.Width, size.Height) * 0.09f;
            graphics.FillEllipse(fill, cx - radius, cy - radius, radius * 2, radius * 2);
            using var pen = new Pen(Color.White, 2);
            graphics.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
        }
    }

    private void DrawGamma(Graphics graphics, Size size)
    {
        graphics.Clear(Color.Black);
        using var white = new SolidBrush(Color.White);
        for (var x = 0; x < size.Width; x += 2)
        {
            graphics.FillRectangle(white, x, 0, 1, size.Height);
        }

        var gray = (int)Math.Round(255 * Math.Pow(0.5, 1 / _gamma));
        using var circle = new SolidBrush(Color.FromArgb(gray, gray, gray));
        var radius = Math.Min(size.Width, size.Height) * 0.15f;
        graphics.FillEllipse(circle, size.Width / 2f - radius, size.Height / 2f - radius, radius * 2, radius * 2);
    }

    private void DrawResponse(Graphics graphics, Size size)
    {
        graphics.Clear(Color.FromArgb(128, 128, 128));
        using var left = new SolidBrush(Color.White);
        using var right = new SolidBrush(Color.Black);
        var x = (float)_responseOffset;
        var y = size.Height / 2f - RESPONSE_BLOCK / 2f;
        graphics.FillRectangle(left, x, y, RESPONSE_BLOCK, RESPONSE_BLOCK);
        graphics.FillRectangle(right, x + RESPONSE_BLOCK + _responseGap, y, RESPONSE_BLOCK, RESPONSE_BLOCK);
    }
}
