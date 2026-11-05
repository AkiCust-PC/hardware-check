using System.Drawing.Drawing2D;
using AkikazuHardwareCheck.Core.Configuration;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Staff drag the picture to move it and scroll or use the slider to resize it, on a preview shaped like the window.</summary>
internal sealed class BackgroundAdjustForm : Form
{
    private const int PREVIEW_WIDTH = 640;
    private const int PREVIEW_SOURCE_SIDE_PIXELS = 1000;
    private const double PERCENT = 100;
    private const double ZOOM_STEP_PER_NOTCH = 1.1;
    private const double WHEEL_NOTCH = 120;
    private const int SLIDER_TICK_PERCENT = 40;

    private readonly Panel _preview = new DoubleBufferedPanel { Cursor = Cursors.SizeAll, Margin = new Padding(0, 0, 0, Ui.SPACING) };
    private readonly TrackBar _zoomBar = new() { Dock = DockStyle.Fill, TickFrequency = SLIDER_TICK_PERCENT };
    private readonly Bitmap _small;
    private Bitmap _turned;
    private Point? _dragFrom;

    public BackgroundAdjustForm(Translator t, Bitmap source, BackgroundTransform transform, BackgroundView view, Size windowSize, Font font)
    {
        Transform = transform;
        View = view;
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["ui.backgroundAdjust"].TrimEnd('…');
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // Previewing from a small copy keeps dragging smooth even with a large photo.
        var scale = Math.Min(1, PREVIEW_SOURCE_SIDE_PIXELS / (double)Math.Max(source.Width, source.Height));
        _small = new Bitmap(source, (int)Math.Max(1, source.Width * scale), (int)Math.Max(1, source.Height * scale));
        _turned = BackgroundPainter.Turn(_small, transform);

        var width = LogicalToDeviceUnits(PREVIEW_WIDTH);
        _preview.Size = new Size(width, Math.Max(1, width * windowSize.Height / Math.Max(1, windowSize.Width)));
        _preview.Paint += (_, e) => BackgroundPainter.Paint(e.Graphics, _turned, _preview.ClientSize, View, InterpolationMode.Bilinear);
        _preview.MouseEnter += (_, _) => _preview.Focus();
        _preview.MouseDown += (_, e) => _dragFrom = e.Location;
        _preview.MouseUp += (_, _) => _dragFrom = null;
        _preview.MouseMove += (_, e) => DragTo(e.Location);
        _preview.MouseWheel += (_, e) => SetZoom(View.Zoom * Math.Pow(ZOOM_STEP_PER_NOTCH, e.Delta / WHEEL_NOTCH));

        _zoomBar.Minimum = (int)(BackgroundView.MIN_ZOOM * PERCENT);
        _zoomBar.Maximum = (int)(BackgroundView.MAX_ZOOM * PERCENT);
        _zoomBar.Value = Math.Clamp((int)(View.Zoom * PERCENT), _zoomBar.Minimum, _zoomBar.Maximum);
        _zoomBar.Scroll += (_, _) => SetZoom(_zoomBar.Value / PERCENT);

        var zoomLabel = Ui.Label();
        zoomLabel.Text = t["ui.backgroundZoom"];
        var zoomRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
        zoomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        zoomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        zoomRow.Controls.Add(zoomLabel, 0, 0);
        zoomRow.Controls.Add(_zoomBar, 1, 0);

        var turns = Ui.Flow();
        turns.Controls.AddRange(
        [
            TurnButton(t["ui.backgroundRotate"], current => current.RotateClockwise()),
            TurnButton(t["ui.backgroundMirrorH"], current => current.MirrorHorizontally()),
            TurnButton(t["ui.backgroundMirrorV"], current => current.MirrorVertically()),
        ]);

        var fit = Ui.Button();
        fit.Text = t["ui.backgroundFit"];
        fit.Click += (_, _) =>
        {
            View = BackgroundView.Default;
            _zoomBar.Value = (int)(View.Zoom * PERCENT);
            _preview.Invalidate();
        };
        turns.Controls.Add(fit);

        var ok = Ui.Button();
        ok.Text = t["ui.ok"];
        ok.DialogResult = DialogResult.OK;
        var cancel = Ui.Button();
        cancel.Text = t["ui.cancel"];
        cancel.DialogResult = DialogResult.Cancel;
        var closing = Ui.Flow();
        closing.Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(Ui.SPACING * 2) };
        layout.Controls.Add(_preview);
        layout.Controls.Add(zoomRow);
        layout.Controls.Add(turns);
        layout.Controls.Add(closing);
        Controls.Add(layout);
        Ui.ScaleButtons(this);
    }

    public BackgroundTransform Transform { get; private set; }

    public BackgroundView View { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _small.Dispose();
            _turned.Dispose();
        }

        base.Dispose(disposing);
    }

    private Button TurnButton(string text, Func<BackgroundTransform, BackgroundTransform> change)
    {
        var button = Ui.Button();
        button.Text = text;
        button.Click += (_, _) =>
        {
            Transform = change(Transform);
            var previous = _turned;
            _turned = BackgroundPainter.Turn(_small, Transform);
            previous.Dispose();
            _preview.Invalidate();
        };
        return button;
    }

    private void DragTo(Point location)
    {
        if (_dragFrom is not { } from)
        {
            return;
        }

        View = View with
        {
            PanX = View.PanX + ((location.X - from.X) / (double)_preview.ClientSize.Width),
            PanY = View.PanY + ((location.Y - from.Y) / (double)_preview.ClientSize.Height),
        };
        _dragFrom = location;
        _preview.Invalidate();
    }

    private void SetZoom(double zoom)
    {
        View = View with { Zoom = Math.Clamp(zoom, BackgroundView.MIN_ZOOM, BackgroundView.MAX_ZOOM) };
        _zoomBar.Value = Math.Clamp((int)(View.Zoom * PERCENT), _zoomBar.Minimum, _zoomBar.Maximum);
        _preview.Invalidate();
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
        }
    }
}
