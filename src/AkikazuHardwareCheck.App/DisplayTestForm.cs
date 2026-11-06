using System.Drawing.Drawing2D;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Full-screen display patterns built with a standard PictureBox. The operator judges the result by eye.</summary>
internal sealed class DisplayTestForm : Form
{
    private const int PATTERN_COUNT = 8;
    private const int CHECKER_SIZE = 64;

    private readonly Translator _t;
    private readonly Screen _screen;
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage };
    private readonly Label _hint = new() { Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Black, ForeColor = Color.White, Height = 32 };
    private int _index;

    public DisplayTestForm(Translator t, Screen screen)
    {
        _t = t;
        _screen = screen;
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
        Shown += (_, _) => ShowPattern(0);
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
        }
    }

    private void Next() => ShowPattern((_index + 1) % PATTERN_COUNT);

    private void Previous() => ShowPattern((_index - 1 + PATTERN_COUNT) % PATTERN_COUNT);

    private void ShowPattern(int index)
    {
        _index = index;
        var old = _picture.Image;
        if (index <= 5)
        {
            // Solid patterns need no bitmap at all: the native PictureBox paints its BackColor.
            _picture.Image = null;
            _picture.BackColor = index switch
            {
                0 => Color.White,
                1 => Color.Black,
                2 => Color.Red,
                3 => Color.Lime,
                4 => Color.Blue,
                _ => Color.Gray,
            };
        }
        else
        {
            _picture.BackColor = Color.Black;
            _picture.Image = CreatePattern(index, ClientSize);
        }

        old?.Dispose();
        _hint.Text = $"{_t["peripheralTest.displayHint"]}  {index + 1}/{PATTERN_COUNT}";
    }

    private static Bitmap CreatePattern(int index, Size size)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        if (index == 6)
        {
            using var brush = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.Black, Color.White, LinearGradientMode.Horizontal);
            graphics.FillRectangle(brush, 0, 0, width, height);
            return bitmap;
        }

        DrawChecker(graphics, width, height);
        return bitmap;
    }

    private static void DrawChecker(Graphics graphics, int width, int height)
    {
        for (var y = 0; y < height; y += CHECKER_SIZE)
        {
            for (var x = 0; x < width; x += CHECKER_SIZE)
            {
                using var brush = new SolidBrush(((x / CHECKER_SIZE) + (y / CHECKER_SIZE)) % 2 == 0 ? Color.White : Color.Black);
                graphics.FillRectangle(brush, x, y, CHECKER_SIZE, CHECKER_SIZE);
            }
        }
    }
}
