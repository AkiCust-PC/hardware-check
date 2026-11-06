using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// A full Japanese 106-key keyboard check: every key is drawn as a native key cap, pressed keys light up, tested keys
/// stay marked, and the maximum number of simultaneous keys is reported. No layout switch is needed for this test.
/// </summary>
internal sealed class KeyboardTestView : UserControl
{
    private sealed record KeyDefinition(Keys Key, string Label, float Width);

    private sealed record GridKey(Keys Key, string Label, int Column, int Row, int ColumnSpan = 1, int RowSpan = 1);

    private readonly Translator _t;
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TableLayoutPanel _main = new() { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Margin = new Padding(0) };
    private readonly Label _progress = Ui.Label();
    private readonly Label _pressedLabel = Ui.Label();
    private readonly Label _rollover = Ui.Label();
    private readonly Font _keyFont;
    private readonly List<(Keys Key, Label Label)> _keyLabels = [];
    private readonly HashSet<Keys> _tested = [];
    private readonly HashSet<Keys> _pressed = [];
    private int _maxPressed;

    public KeyboardTestView(Translator t, Font font)
    {
        _t = t;
        Font = font;
        _keyFont = new Font(font.FontFamily, 7f);
        Dock = DockStyle.Fill;
        Padding = new Padding(Ui.SPACING);

        var reset = Ui.Button();
        reset.Text = t["peripheralTest.clear"];
        reset.Click += (_, _) => Reset();

        var hint = new Label
        {
            Text = t["keyboardTest.hint"],
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(900), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, Ui.SPACING),
        };

        var blocks = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            WrapContents = false,
            Margin = new Padding(0),
        };
        BuildMain();
        blocks.Controls.Add(_main);
        blocks.Controls.Add(BuildNavigation());
        blocks.Controls.Add(BuildNumpad());
        _scroll.Controls.Add(blocks);

        var top = Ui.Flow();
        top.WrapContents = false;
        top.Controls.Add(reset);

        var status = Ui.Flow();
        status.WrapContents = false;
        status.Controls.AddRange([_progress, _pressedLabel, _rollover]);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(hint, 0, 0);
        root.Controls.Add(top, 0, 1);
        root.Controls.Add(_scroll, 0, 2);
        root.Controls.Add(status, 0, 3);
        Controls.Add(root);
        UpdateStatus();
    }

    public void MarkKeyDown(Keys key)
    {
        var labels = _keyLabels.Where(item => item.Key == key).Select(item => item.Label).ToList();
        if (labels.Count == 0)
        {
            return;
        }

        _tested.Add(key);
        _pressed.Add(key);
        _maxPressed = Math.Max(_maxPressed, _pressed.Count);
        PaintKeys();
        UpdateStatus();
    }

    public void MarkKeyUp(Keys key)
    {
        if (_pressed.Remove(key))
        {
            PaintKeys();
            UpdateStatus();
        }
    }

    public void Reset()
    {
        _tested.Clear();
        _pressed.Clear();
        _maxPressed = 0;
        PaintKeys();
        UpdateStatus();
    }

    private void BuildMain()
    {
        _main.Controls.Clear();
        _main.RowStyles.Clear();
        _main.RowCount = 0;
        foreach (var row in MainRows())
        {
            var line = BuildRow(row);
            _main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _main.Controls.Add(line, 0, _main.RowCount++);
        }
    }

    private TableLayoutPanel BuildRow(IReadOnlyList<KeyDefinition> row)
    {
        var unit = LogicalToDeviceUnits(34);
        var width = (int)Math.Round(row.Sum(key => key.Width) * unit);
        var line = new TableLayoutPanel
        {
            AutoSize = false,
            Width = width,
            Height = LogicalToDeviceUnits(34),
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(0, 1, 0, 1),
            RowCount = 1,
            ColumnCount = row.Count,
        };
        line.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(34)));
        for (var index = 0; index < row.Count; index++)
        {
            var key = row[index];
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (float)Math.Round(unit * key.Width)));
            line.Controls.Add(MakeKey(key.Key, key.Label), index, 0);
        }

        return line;
    }

    private TableLayoutPanel BuildNavigation()
    {
        var block = Grid(3, 5);
        AddGrid(block, new GridKey(Keys.Insert, "Ins", 0, 0));
        AddGrid(block, new GridKey(Keys.Home, "Hom", 1, 0));
        AddGrid(block, new GridKey(Keys.PageUp, "PgU", 2, 0));
        AddGrid(block, new GridKey(Keys.Delete, "Del", 0, 1));
        AddGrid(block, new GridKey(Keys.End, "End", 1, 1));
        AddGrid(block, new GridKey(Keys.PageDown, "PgD", 2, 1));
        AddGrid(block, new GridKey(Keys.Up, "↑", 1, 2));
        AddGrid(block, new GridKey(Keys.Left, "←", 0, 3));
        AddGrid(block, new GridKey(Keys.Down, "↓", 1, 3));
        AddGrid(block, new GridKey(Keys.Right, "→", 2, 3));
        return block;
    }

    private TableLayoutPanel BuildNumpad()
    {
        var block = Grid(4, 5);
        AddGrid(block, new GridKey(Keys.NumLock, "Num", 0, 0));
        AddGrid(block, new GridKey(Keys.Divide, "/", 1, 0));
        AddGrid(block, new GridKey(Keys.Multiply, "*", 2, 0));
        AddGrid(block, new GridKey(Keys.Subtract, "-", 3, 0));
        AddGrid(block, new GridKey(Keys.NumPad7, "7", 0, 1));
        AddGrid(block, new GridKey(Keys.NumPad8, "8", 1, 1));
        AddGrid(block, new GridKey(Keys.NumPad9, "9", 2, 1));
        AddGrid(block, new GridKey(Keys.Add, "+", 3, 1, RowSpan: 2));
        AddGrid(block, new GridKey(Keys.NumPad4, "4", 0, 2));
        AddGrid(block, new GridKey(Keys.NumPad5, "5", 1, 2));
        AddGrid(block, new GridKey(Keys.NumPad6, "6", 2, 2));
        AddGrid(block, new GridKey(Keys.NumPad1, "1", 0, 3));
        AddGrid(block, new GridKey(Keys.NumPad2, "2", 1, 3));
        AddGrid(block, new GridKey(Keys.NumPad3, "3", 2, 3));
        AddGrid(block, new GridKey(Keys.Enter, "Ent", 3, 3, RowSpan: 2));
        AddGrid(block, new GridKey(Keys.NumPad0, "0", 0, 4, ColumnSpan: 2));
        AddGrid(block, new GridKey(Keys.Decimal, ".", 2, 4));
        return block;
    }

    private TableLayoutPanel Grid(int columns, int rows)
    {
        var unit = LogicalToDeviceUnits(34);
        var grid = new TableLayoutPanel
        {
            AutoSize = false,
            Width = columns * unit,
            Height = rows * LogicalToDeviceUnits(34),
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(Ui.SPACING, 1, 0, 1),
            ColumnCount = columns,
            RowCount = rows,
        };
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, unit));
        }

        for (var row = 0; row < rows; row++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(34)));
        }

        return grid;
    }

    private void AddGrid(TableLayoutPanel grid, GridKey key)
    {
        var label = MakeKey(key.Key, key.Label);
        grid.Controls.Add(label, key.Column, key.Row);
        if (key.ColumnSpan > 1)
        {
            grid.SetColumnSpan(label, key.ColumnSpan);
        }

        if (key.RowSpan > 1)
        {
            grid.SetRowSpan(label, key.RowSpan);
        }
    }

    private Label MakeKey(Keys key, string label)
    {
        var control = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(1),
            Padding = new Padding(0),
            AutoSize = false,
            Font = _keyFont,
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText,
            UseMnemonic = false,
        };
        _keyLabels.Add((key, control));
        return control;
    }

    private static IEnumerable<KeyDefinition[]> MainRows()
    {
        yield return
        [
            new(Keys.Escape, "Esc", 1),
            .. Enumerable.Range(1, 12).Select(index => new KeyDefinition((Keys)((int)Keys.F1 + index - 1), $"F{index}", 1)),
            new(Keys.PrintScreen, "Prt", 1),
            new(Keys.Scroll, "Scr", 1),
            new(Keys.Pause, "Pau", 1),
        ];

        yield return
        [
            new((Keys)0xDF, "半角/全角", 1.4f),
            .. Enumerable.Range(0, 10).Select(index => new KeyDefinition((Keys)((int)Keys.D0 + ((index + 1) % 10)), ((index + 1) % 10).ToString(), 1)),
            new(Keys.OemMinus, "-", 1),
            new(Keys.Oemplus, "^", 1),
            new(Keys.OemPipe, "¥", 1),
            new(Keys.Back, "Bksp", 2),
        ];

        yield return
        [
            new(Keys.Tab, "Tab", 1.5f),
            .. "QWERTYUIOP".Select(letter => new KeyDefinition((Keys)Enum.Parse(typeof(Keys), letter.ToString()), letter.ToString(), 1)),
            new(Keys.OemOpenBrackets, "@", 1),
            new(Keys.OemCloseBrackets, "[", 1),
            new(Keys.Enter, "Ent", 2.5f),
        ];

        yield return
        [
            new(Keys.CapsLock, "Caps", 1.8f),
            .. "ASDFGHJKL".Select(letter => new KeyDefinition((Keys)Enum.Parse(typeof(Keys), letter.ToString()), letter.ToString(), 1)),
            new(Keys.OemSemicolon, ";", 1),
            new(Keys.OemQuotes, ":", 1),
            new(Keys.OemBackslash, "]", 1),
        ];

        yield return
        [
            new(Keys.LShiftKey, "Shift", 2.3f),
            .. "ZXCVBNM".Select(letter => new KeyDefinition((Keys)Enum.Parse(typeof(Keys), letter.ToString()), letter.ToString(), 1)),
            new(Keys.Oemcomma, ",", 1),
            new(Keys.OemPeriod, ".", 1),
            new(Keys.OemQuestion, "/", 1),
            new(Keys.Oem102, "\\", 1),
            new(Keys.RShiftKey, "Shift", 2.3f),
        ];

        yield return
        [
            new(Keys.LControlKey, "Ctrl", 1.2f),
            new(Keys.LWin, "Win", 1),
            new(Keys.LMenu, "Alt", 1.2f),
            new((Keys)0x1D, "無変換", 1.3f),
            new(Keys.Space, "Space", 3.2f),
            new((Keys)0x1C, "変換", 1.3f),
            new((Keys)0x15, "かな", 1.2f),
            new((Keys)0x19, "英数", 1.2f),
            new(Keys.RMenu, "Alt", 1.2f),
            new(Keys.RWin, "Win", 1),
            new(Keys.Apps, "Men", 1.2f),
            new(Keys.RControlKey, "Ctrl", 1.2f),
        ];
    }

    private void PaintKeys()
    {
        foreach (var (key, label) in _keyLabels)
        {
            if (_pressed.Contains(key))
            {
                label.BackColor = SystemColors.Highlight;
                label.ForeColor = SystemColors.HighlightText;
            }
            else if (_tested.Contains(key))
            {
                label.BackColor = Color.FromArgb(198, 239, 206);
                label.ForeColor = Color.FromArgb(0, 97, 0);
            }
            else
            {
                label.BackColor = SystemColors.Control;
                label.ForeColor = SystemColors.ControlText;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keyFont.Dispose();
        }
    }

    private void UpdateStatus()
    {
        _progress.Text = _t["keyboardTest.progress", _tested.Count, _keyLabels.Select(item => item.Key).Distinct().Count()];
        var pressed = _pressed.SelectMany(key => _keyLabels.Where(item => item.Key == key).Select(item => item.Label.Text).Take(1)).Order(StringComparer.Ordinal).ToList();
        _pressedLabel.Text = _t["keyboardTest.pressed", pressed.Count == 0 ? "—" : string.Join(" + ", pressed)];
        _rollover.Text = _t["keyboardTest.rollover", _pressed.Count, _maxPressed];
    }
}
