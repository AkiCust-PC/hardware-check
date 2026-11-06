using System.Globalization;
using AkikazuHardwareCheck.Core.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// A small borderless monitor that can sit against a screen edge or expand when dragged away. It reads the existing
/// live snapshot and never opens a second sensor source.
/// </summary>
internal sealed class FloatingPerformanceForm : Form
{
    private const int EDGE_WIDTH = 220;
    private const int EXPANDED_WIDTH = 380;
    private const int SNAP_DISTANCE = 28;
    private const int INITIAL_HEIGHT = 320;

    private readonly Translator _t;
    private readonly Func<LiveSnapshot> _readSnapshot;
    private readonly Label _cpuTemperature = ValueLabel();
    private readonly Label _gpuTemperature = ValueLabel();
    private readonly Label _cpuLoad = ValueLabel();
    private readonly Label _gpuLoad = ValueLabel();
    private readonly Label _memory = ValueLabel();
    private readonly Label _result = Ui.Label();
    private readonly Button _clean = Ui.Button();
    private readonly Button _close = new() { Text = "×", AutoSize = false, Size = new Size(28, 28), FlatStyle = FlatStyle.System };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private Point _dragCursor;
    private Point _dragStart;
    private bool _dragging;
    private bool _snapped;
    private bool _cleaning;

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
        ClientSize = new Size(EDGE_WIDTH, INITIAL_HEIGHT);
        MinimumSize = new Size(EDGE_WIDTH, 260);

        _clean.Text = t["floating.clean"];
        _clean.MinimumSize = new Size(0, Ui.BUTTON_MIN_HEIGHT);
        _clean.Click += async (_, _) => await CleanMemoryAsync();
        _close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _close.Click += (_, _) => Hide();

        var title = new Label
        {
            Text = t["floating.title"],
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, Ui.SPACING),
        };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(title, 0, 0);
        top.Controls.Add(_close, 1, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, AutoSize = true, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddMetric(body, t["floating.cpuTemperature"], _cpuTemperature, 0, 0);
        AddMetric(body, t["floating.gpuTemperature"], _gpuTemperature, 1, 0);
        AddMetric(body, t["floating.cpuLoad"], _cpuLoad, 0, 2);
        AddMetric(body, t["floating.gpuLoad"], _gpuLoad, 1, 2);
        AddMetric(body, t["floating.memory"], _memory, 0, 4);
        body.Controls.Add(_clean, 0, 6);
        body.SetColumnSpan(_clean, 2);
        body.Controls.Add(_result, 0, 7);
        body.SetColumnSpan(_result, 2);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.SPACING), Margin = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(body, 0, 1);
        AttachDrag(root);
        Controls.Add(root);

        _timer.Tick += (_, _) => RefreshValues();
        Shown += (_, _) =>
        {
            Width = DpiScale(_snapped ? EDGE_WIDTH : EXPANDED_WIDTH);
            Height = DpiScale(INITIAL_HEIGHT);
            SnapToNearestEdge();
            RefreshValues();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Dispose();
        Resize += (_, _) => ApplyWidth();
    }

    private void AttachDrag(Control control)
    {
        if (control is not Button)
        {
            control.MouseDown += BeginDrag;
            control.MouseMove += MoveDrag;
            control.MouseUp += EndDrag;
            control.DoubleClick += ToggleSnap;
        }

        foreach (Control child in control.Controls)
        {
            AttachDrag(child);
        }
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragCursor = Cursor.Position;
        _dragStart = Location;
    }

    private void MoveDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Location = new Point(
            _dragStart.X + Cursor.Position.X - _dragCursor.X,
            _dragStart.Y + Cursor.Position.Y - _dragCursor.Y);
    }

    private void EndDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        SnapToNearestEdge();
    }

    private void ToggleSnap(object? sender, EventArgs e)
    {
        if (_snapped)
        {
            _snapped = false;
            Width = DpiScale(EXPANDED_WIDTH);
        }
        else
        {
            SnapToNearestEdge(force: true);
        }
    }

    private void AddMetric(TableLayoutPanel panel, string title, Label value, int column, int row)
    {
        var label = new Label { Text = title, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0) };
        panel.Controls.Add(label, column, row);
        panel.Controls.Add(value, column, row + 1);
    }

    private void RefreshValues()
    {
        var sample = _readSnapshot();
        _cpuTemperature.Text = Temperature(sample.CpuTemperature);
        _gpuTemperature.Text = Temperature(sample.GpuTemperature);
        _cpuLoad.Text = Percent(sample.CpuLoad);
        _gpuLoad.Text = Percent(sample.GpuLoad);
        _memory.Text = Percent(sample.MemoryLoad);
        if (!_cleaning)
        {
            _result.Text = "";
        }
    }

    private async Task CleanMemoryAsync()
    {
        if (MessageBox.Show(this, _t["floating.confirm"], _t["floating.clean"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _cleaning = true;
        _clean.Enabled = false;
        _result.Text = _t["floating.cleaning"];
        try
        {
            var result = await Task.Run(MemoryOptimizer.CleanOtherProcesses);
            _result.Text = _t["floating.done", result.Succeeded, result.Skipped];
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            _result.Text = exception.Message;
        }
        finally
        {
            _cleaning = false;
            _clean.Enabled = true;
        }
    }

    private void SnapToNearestEdge(bool force = false)
    {
        var screen = Screen.FromControl(this).WorkingArea;
        var leftDistance = Math.Abs(Left - screen.Left);
        var rightDistance = Math.Abs(screen.Right - Right);
        var snap = force || Math.Min(leftDistance, rightDistance) <= DpiScale(SNAP_DISTANCE);
        if (!snap)
        {
            _snapped = false;
            Width = DpiScale(EXPANDED_WIDTH);
            return;
        }

        _snapped = true;
        Width = DpiScale(EDGE_WIDTH);
        Left = rightDistance <= leftDistance ? screen.Right - Width : screen.Left;
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
    }

    private void ApplyWidth()
    {
        if (_snapped)
        {
            Width = DpiScale(EDGE_WIDTH);
        }
    }

    private int DpiScale(int logical) => (int)Math.Round(logical * DeviceDpi / 96d);

    private static Label ValueLabel() => new() { AutoSize = true, Font = new Font((SystemFonts.MessageBoxFont ?? Control.DefaultFont).FontFamily, 12f, FontStyle.Bold), Margin = new Padding(0, 0, 0, Ui.SPACING) };

    private static string Temperature(double? value) =>
        value is { } number ? $"{number.ToString("0.#", CultureInfo.InvariantCulture)} °C" : "—";

    private static string Percent(double? value) =>
        value is { } number ? $"{number.ToString("0", CultureInfo.InvariantCulture)}%" : "—";
}
