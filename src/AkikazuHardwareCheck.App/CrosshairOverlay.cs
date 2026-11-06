using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

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
