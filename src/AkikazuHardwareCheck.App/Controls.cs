using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.App;

/// <summary>One row of an <see cref="InfoView"/>. A header row spans the width and has no value.</summary>
internal sealed record InfoRow(string Label, string Value, Color? Color = null, bool IsHeader = false);

/// <summary>
/// A label-and-value list built only from system labels in a table. Text is never cut off: a label in a table column
/// wraps at the column width and its row grows, and the outer panel scrolls when the rows no longer fit.
/// A ListView cannot do this, because all its rows share one height.
/// </summary>
internal sealed class InfoView : Panel
{
    private const int LABEL_WIDTH = 110;

    private readonly TableLayoutPanel _table = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top,
        ColumnCount = 2,
        Margin = new Padding(0),
    };

    public InfoView()
    {
        AutoScroll = true;
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_table);
        Resize += (_, _) => ApplyWidthLimits();
    }

    public void SetRows(IReadOnlyList<InfoRow> rows)
    {
        _table.SuspendLayout();
        var old = _table.Controls.Cast<Control>().ToList();
        _table.Controls.Clear();
        old.ForEach(control => control.Dispose());
        _table.RowStyles.Clear();
        _table.RowCount = rows.Count;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = Cell(row.Label, row.Color ?? Ui.MutedTextColor);
            _table.Controls.Add(label, 0, index);
            if (row.IsHeader)
            {
                label.Font = new Font(Font, FontStyle.Bold);
                _table.SetColumnSpan(label, 2);
                continue;
            }

            _table.Controls.Add(Cell(row.Value, row.Color ?? SystemColors.ControlText), 1, index);
        }

        _table.ResumeLayout();
        ApplyWidthLimits();
    }

    /// <summary>
    /// Keep the left panel from growing to the width of its longest value. Values wrap inside the available width
    /// instead, so the monitor column still has room at the default window size.
    /// </summary>
    private void ApplyWidthLimits()
    {
        if (ClientSize.Width <= 1)
        {
            return;
        }

        var available = Math.Max(1, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - (Ui.SPACING * 2));
        var labelWidth = Math.Min(LogicalToDeviceUnits(LABEL_WIDTH), Math.Max(1, available / 3));
        var valueWidth = Math.Max(1, available - labelWidth);
        _table.SuspendLayout();
        for (var row = 0; row < _table.RowCount; row++)
        {
            if (_table.GetControlFromPosition(0, row) is { } label)
            {
                label.MaximumSize = new Size(labelWidth, 0);
            }

            if (_table.GetControlFromPosition(1, row) is { } value)
            {
                value.MaximumSize = new Size(valueWidth, 0);
            }
        }

        _table.ResumeLayout();
    }

    /// <summary>Changes one row's text in place, without rebuilding the list.</summary>
    public void UpdateValue(string label, string value)
    {
        for (var row = 0; row < _table.RowCount; row++)
        {
            if (_table.GetControlFromPosition(0, row)?.Text == label && _table.GetControlFromPosition(1, row) is { } cell)
            {
                cell.Text = value;
                return;
            }
        }
    }

    private static Label Cell(string text, Color color) => new()
    {
        Text = text,
        ForeColor = color,
        AutoSize = true,
        UseMnemonic = false,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        Margin = new Padding(0, Ui.SPACING / 2, Ui.SPACING * 2, Ui.SPACING / 2),
    };
}

/// <summary>One live reading: a caption, a large value and, where it has a scale, a system progress bar that takes the system's own state colors.</summary>
internal sealed class MetricTile : TableLayoutPanel
{
    private const int BAR_HEIGHT = 6;
    private const float VALUE_FONT_SIZE = 11f;

    // PBM_SETSTATE from CommCtrl.h: Windows draws these states green, red and yellow in its own style.
    private const uint PBM_SETSTATE = 0x0410;
    private const nint PBST_NORMAL = 1;
    private const nint PBST_ERROR = 2;
    private const nint PBST_PAUSED = 3;

    private readonly Label _caption = new() { AutoSize = true, Margin = new Padding(0) };
    private readonly Label _value = new() { AutoSize = true, Text = "—", Margin = new Padding(0, 0, 0, 2) };
    private readonly ProgressBar _bar = new() { Maximum = Ui.PERCENT_MAX, Dock = DockStyle.Top, Margin = new Padding(0), Visible = false };

    public MetricTile()
    {
        ColumnCount = 1;
        RowCount = 3;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, Ui.SPACING, 0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bar.Height = LogicalToDeviceUnits(BAR_HEIGHT);
        Controls.Add(_caption, 0, 0);
        Controls.Add(_value, 0, 1);
        Controls.Add(_bar, 0, 2);
    }

    public void SetCaption(string caption)
    {
        _caption.Text = caption;
        _caption.ForeColor = Ui.MutedTextColor;
        _value.Font = new Font(Font.FontFamily, VALUE_FONT_SIZE, FontStyle.Bold);
    }

    public void SetValueSize(float size) => _value.Font = new Font(Font.FontFamily, size, FontStyle.Bold);

    public void Show(string value, double? fraction, MetricLevel level)
    {
        _value.Text = value;
        _value.ForeColor = level switch
        {
            MetricLevel.Alert => Ui.AlertColor,
            MetricLevel.Warning => Ui.WarningColor,
            _ => SystemColors.ControlText,
        };
        _bar.Visible = fraction is not null;
        if (fraction is not { } amount)
        {
            return;
        }

        _bar.Value = (int)(Math.Clamp(amount, 0, 1) * Ui.PERCENT_MAX);
        SendMessageW(_bar.Handle, PBM_SETSTATE, level switch
        {
            MetricLevel.Alert => PBST_ERROR,
            MetricLevel.Warning => PBST_PAUSED,
            _ => PBST_NORMAL,
        }, 0);
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
