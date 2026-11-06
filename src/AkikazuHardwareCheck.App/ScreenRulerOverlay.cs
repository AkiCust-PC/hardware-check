using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AkikazuHardwareCheck.App;

/// <summary>A click-through border ruler. Ticks are based on the physical primary screen pixels.</summary>
internal sealed class ScreenRulerOverlay : Form
{
    private const int TICK_SPACING = 50;
    private const int MAJOR_TICK = 100;
    private static readonly Color RulerColor = Color.FromArgb(220, 255, 80, 80);

    public ScreenRulerOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        Bounds = new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000080 | 0x08000000 | 0x00080000;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0084)
        {
            message.Result = new IntPtr(-1);
            return;
        }

        base.WndProc(ref message);
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

        var width = ClientSize.Width;
        var height = ClientSize.Height;
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(RulerColor, 1))
        using (var brush = new SolidBrush(RulerColor))
        using (var font = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            for (var x = 0; x < width; x += TICK_SPACING)
            {
                var major = x % MAJOR_TICK == 0;
                var length = major ? 18 : 10;
                graphics.DrawLine(pen, x, 0, x, length);
                graphics.DrawLine(pen, x, height - length, x, height);
                if (major)
                {
                    graphics.DrawString(x.ToString(), font, brush, x + 3, 2);
                }
            }

            for (var y = 0; y < height; y += TICK_SPACING)
            {
                var major = y % MAJOR_TICK == 0;
                var length = major ? 18 : 10;
                graphics.DrawLine(pen, 0, y, length, y);
                graphics.DrawLine(pen, width - length, y, width, y);
                if (major)
                {
                    graphics.DrawString(y.ToString(), font, brush, 2, y + 2);
                }
            }
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

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, int colorKey, ref BlendFunction blend, int flags);
}
