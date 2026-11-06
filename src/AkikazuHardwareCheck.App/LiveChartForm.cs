using System.Drawing.Drawing2D;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// A modeless graph of the readings already shown in the main window. It only asks the main window for its latest
/// snapshot, so opening it never starts a second sensor sampler or a second hardware enumeration.
/// </summary>
internal sealed class LiveChartForm : Form
{
    private const int MAX_SAMPLES = 120;
    private const int CHART_PADDING = 36;
    private const int LABEL_HEIGHT = 22;

    private static readonly Color CpuColor = Color.FromArgb(0, 120, 215);
    private static readonly Color GpuColor = Color.FromArgb(216, 59, 1);
    private static readonly Color MemoryColor = Color.FromArgb(16, 124, 16);
    private static readonly Color DiskColor = Color.FromArgb(157, 93, 0);
    private static readonly Color UploadColor = Color.FromArgb(0, 120, 215);
    private static readonly Color DownloadColor = Color.FromArgb(216, 59, 1);

    private readonly Translator _t;
    private readonly Func<LiveSnapshot> _readSnapshot;
    private readonly List<LiveSnapshot> _samples = [];
    private readonly PictureBox _utilization = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage, BorderStyle = BorderStyle.FixedSingle };
    private readonly PictureBox _network = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage, BorderStyle = BorderStyle.FixedSingle };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private DateTimeOffset? _lastAt;

    public LiveChartForm(Translator t, Func<LiveSnapshot> readSnapshot, Font font)
    {
        _t = t;
        _readSnapshot = readSnapshot;
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["liveChart.title"];
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(900), LogicalToDeviceUnits(600));
        MinimumSize = new Size(LogicalToDeviceUnits(700), LogicalToDeviceUnits(450));

        var utilizationLegend = BuildLegend(t["liveChart.utilization"],
            [(t["liveChart.cpu"], CpuColor), (t["liveChart.gpu"], GpuColor), (t["liveChart.memory"], MemoryColor), (t["liveChart.disk"], DiskColor)]);
        var networkLegend = BuildLegend(t["liveChart.network"],
            [(t["liveChart.upload"], UploadColor), (t["liveChart.download"], DownloadColor)]);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(Ui.SPACING) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.Controls.Add(utilizationLegend, 0, 0);
        root.Controls.Add(_utilization, 0, 1);
        root.Controls.Add(networkLegend, 0, 2);
        root.Controls.Add(_network, 0, 3);
        Controls.Add(root);

        _timer.Tick += (_, _) => Tick();
        Shown += (_, _) =>
        {
            ReadOnce();
            _timer.Start();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _utilization.Image?.Dispose();
            _network.Image?.Dispose();
        };
        Resize += (_, _) => Render();
    }

    private static FlowLayoutPanel BuildLegend(string title, (string Text, Color Color)[] items)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, Ui.SPACING / 2) };
        var heading = new Label { AutoSize = true, Text = title, Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 0, Ui.SPACING * 2, 0) };
        panel.Controls.Add(heading);
        foreach (var (text, color) in items)
        {
            panel.Controls.Add(new Label { AutoSize = true, Text = text, ForeColor = color, Margin = new Padding(0, 0, Ui.SPACING, 0) });
        }

        return panel;
    }

    private void Tick()
    {
        ReadOnce();
        Render();
    }

    private void ReadOnce()
    {
        var sample = _readSnapshot();
        if (sample.At == _lastAt)
        {
            return;
        }

        _lastAt = sample.At;
        _samples.Add(sample);
        if (_samples.Count > MAX_SAMPLES)
        {
            _samples.RemoveAt(0);
        }
    }

    private void Render()
    {
        if (_samples.Count == 0)
        {
            return;
        }

        DrawPercentChart();
        DrawNetworkChart();
    }

    private void DrawPercentChart()
    {
        var width = Math.Max(1, _utilization.Width);
        var height = Math.Max(1, _utilization.Height);
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SystemColors.Window);
        var plot = new Rectangle(CHART_PADDING, LABEL_HEIGHT, Math.Max(1, width - CHART_PADDING - 8), Math.Max(1, height - LABEL_HEIGHT - 12));
        DrawGrid(graphics, plot, 100, value => $"{value:0}%");
        DrawSeries(graphics, plot, 100, CpuColor, sample => sample.CpuLoad);
        DrawSeries(graphics, plot, 100, GpuColor, sample => sample.GpuLoad);
        DrawSeries(graphics, plot, 100, MemoryColor, sample => sample.MemoryLoad);
        DrawSeries(graphics, plot, 100, DiskColor, sample => sample.DiskBusy);
        ReplaceImage(_utilization, bitmap);
    }

    private void DrawNetworkChart()
    {
        var width = Math.Max(1, _network.Width);
        var height = Math.Max(1, _network.Height);
        var maximum = Math.Max(1, _samples.SelectMany(sample => new[] { sample.UploadBytesPerSecond ?? 0, sample.DownloadBytesPerSecond ?? 0 }).Max());
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SystemColors.Window);
        var plot = new Rectangle(CHART_PADDING, LABEL_HEIGHT, Math.Max(1, width - CHART_PADDING - 8), Math.Max(1, height - LABEL_HEIGHT - 12));
        DrawGrid(graphics, plot, maximum, value => Ui.FormatRate(value));
        DrawSeries(graphics, plot, maximum, UploadColor, sample => sample.UploadBytesPerSecond);
        DrawSeries(graphics, plot, maximum, DownloadColor, sample => sample.DownloadBytesPerSecond);
        ReplaceImage(_network, bitmap);
    }

    private static void ReplaceImage(PictureBox box, Bitmap bitmap)
    {
        var old = box.Image;
        box.Image = bitmap;
        old?.Dispose();
    }

    private static void DrawGrid(Graphics graphics, Rectangle plot, double maximum, Func<double, string> format)
    {
        using var gridPen = new Pen(SystemColors.ControlLight);
        using var textBrush = new SolidBrush(SystemColors.GrayText);
        using var font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? Control.DefaultFont.FontFamily, 8f);
        for (var step = 0; step <= 4; step++)
        {
            var value = maximum * step / 4;
            var y = plot.Bottom - (float)(plot.Height * step / 4d);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var text = format(value);
            var size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, textBrush, plot.Left - size.Width - 4, y - (size.Height / 2));
        }

        graphics.DrawRectangle(gridPen, plot);
    }

    private void DrawSeries(Graphics graphics, Rectangle plot, double maximum, Color color, Func<LiveSnapshot, double?> selector)
    {
        if (_samples.Count < 2)
        {
            return;
        }

        using var pen = new Pen(color, 1.5f);
        var points = new List<PointF>();
        for (var index = 0; index < _samples.Count; index++)
        {
            var value = selector(_samples[index]);
            if (value is not { } number)
            {
                DrawPoints(graphics, pen, points);
                points.Clear();
                continue;
            }

            var x = plot.Left + (float)(plot.Width * index / (double)(_samples.Count - 1));
            var y = plot.Bottom - (float)(plot.Height * Math.Clamp(number / maximum, 0, 1));
            points.Add(new PointF(x, y));
        }

        DrawPoints(graphics, pen, points);
    }

    private static void DrawPoints(Graphics graphics, Pen pen, List<PointF> points)
    {
        if (points.Count >= 2)
        {
            graphics.DrawLines(pen, points.ToArray());
        }
        else if (points.Count == 1)
        {
            graphics.FillEllipse(pen.Brush, points[0].X - 1, points[0].Y - 1, 2, 2);
        }
    }
}
