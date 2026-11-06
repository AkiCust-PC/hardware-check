using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// A visual keyboard laid out like the one on the hardware test page: ANSI and JIS rows, with pressed keys lit while
/// held and tested keys marked afterwards. Native labels are used instead of drawing so the window keeps the system look.
/// </summary>
internal sealed class KeyboardTestView : UserControl
{
    private enum KeyboardLayoutKind
    {
        Ansi,
        Jis,
    }

    private sealed record KeyDefinition(Keys Key, string Label, float Width);

    private static readonly (Keys Key, string Label)[] JisOnly =
    [
        ((Keys)0xDC, "¥"),
        ((Keys)0xE2, "ろ"),
        ((Keys)0x1D, "無変換"),
        ((Keys)0x1C, "変換"),
        ((Keys)0x15, "かな"),
        ((Keys)0x19, "英数"),
    ];

    // 0xDC is also the ANSI backslash key, so it cannot be used as proof of a JIS keyboard.
    private static readonly HashSet<Keys> JisDetectionKeys = [.. JisOnly.Select(item => item.Key).Where(key => (int)key != 0xDC)];

    private readonly Translator _t;
    private readonly ComboBox _layout = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly Label _progress = Ui.Label();
    private readonly Label _pressedLabel = Ui.Label();
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TableLayoutPanel _board = new() { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Margin = new Padding(0) };
    private readonly Dictionary<Keys, Label> _keyLabels = [];
    private readonly HashSet<Keys> _tested = [];
    private readonly HashSet<Keys> _pressed = [];
    private KeyboardLayoutKind _currentLayout = KeyboardLayoutKind.Jis;
    private bool _isManualLayout;

    public KeyboardTestView(Translator t, Font font)
    {
        _t = t;
        Font = font;
        Dock = DockStyle.Fill;
        Padding = new Padding(Ui.SPACING);

        var reset = Ui.Button();
        reset.Text = t["peripheralTest.clear"];
        reset.Click += (_, _) => Reset();
        _layout.Items.AddRange([t["keyboardTest.ansi"], t["keyboardTest.jis"]]);
        _layout.SelectedIndex = 1;
        _layout.SelectedIndexChanged += (_, _) =>
        {
            if (_layout.SelectedIndex < 0)
            {
                return;
            }

            _currentLayout = _layout.SelectedIndex == 0 ? KeyboardLayoutKind.Ansi : KeyboardLayoutKind.Jis;
            _isManualLayout = true;
            Reset();
            BuildRows();
        };

        var layoutRow = Ui.Flow();
        layoutRow.WrapContents = false;
        layoutRow.Controls.Add(new Label { Text = t["keyboardTest.layout"], AutoSize = true, Margin = new Padding(0, 6, Ui.SPACING, 0) });
        layoutRow.Controls.Add(_layout);
        layoutRow.Controls.Add(reset);

        _board.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _scroll.Controls.Add(_board);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hint = new Label
        {
            Text = t["keyboardTest.hint"],
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(760), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, Ui.SPACING),
        };
        root.Controls.Add(hint, 0, 0);
        root.Controls.Add(layoutRow, 0, 1);
        root.Controls.Add(_scroll, 0, 2);
        root.Controls.Add(_progress, 0, 3);
        var bottom = Ui.Flow();
        bottom.Controls.Add(_pressedLabel);
        root.Controls.Add(bottom, 0, 4);
        Controls.Add(root);

        BuildRows();
        UpdateStatus();
    }

    public void MarkKeyDown(Keys key)
    {
        if (!_isManualLayout && JisDetectionKeys.Contains(key))
        {
            _currentLayout = KeyboardLayoutKind.Jis;
            _layout.SelectedIndex = 1;
            BuildRows();
        }

        if (!_keyLabels.ContainsKey(key))
        {
            return;
        }

        _tested.Add(key);
        _pressed.Add(key);
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
        PaintKeys();
        UpdateStatus();
    }

    private void BuildRows()
    {
        _board.SuspendLayout();
        _board.Controls.Clear();
        _board.RowStyles.Clear();
        _board.RowCount = 0;
        _keyLabels.Clear();

        foreach (var row in Rows())
        {
            var line = BuildRow(row);
            _board.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _board.Controls.Add(line, 0, _board.RowCount++);
        }

        if (_currentLayout == KeyboardLayoutKind.Jis)
        {
            var jis = JisOnly.Select(item => new KeyDefinition(item.Key, item.Label, 1.7f)).ToArray();
            var line = BuildRow(jis);
            _board.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _board.Controls.Add(line, 0, _board.RowCount++);
        }

        _board.ResumeLayout();
        PaintKeys();
        UpdateStatus();
    }

    private TableLayoutPanel BuildRow(IReadOnlyList<KeyDefinition> row)
    {
        var unit = LogicalToDeviceUnits(44);
        var width = (int)Math.Round(row.Sum(key => key.Width) * unit);
        var line = new TableLayoutPanel
        {
            AutoSize = false,
            Width = width,
            Height = LogicalToDeviceUnits(40),
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(0),
            RowCount = 1,
            ColumnCount = row.Count,
        };
        line.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(40)));
        for (var index = 0; index < row.Count; index++)
        {
            var key = row[index];
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (float)Math.Round(unit * key.Width)));
            var label = new Label
            {
                Text = key.Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(1),
                AutoSize = false,
                BackColor = SystemColors.Control,
                ForeColor = SystemColors.ControlText,
                UseMnemonic = false,
            };
            _keyLabels[key.Key] = label;
            line.Controls.Add(label, index, 0);
        }

        return line;
    }

    private IEnumerable<KeyDefinition[]> Rows()
    {
        yield return FunctionKeys();
        yield return NumberRow();
        yield return TopLetterRow();
        yield return HomeLetterRow();
        yield return BottomLetterRow();
        yield return ModifierRow();
    }

    private static KeyDefinition[] FunctionKeys() =>
    [
        new(Keys.Escape, "Esc", 1),
        .. Enumerable.Range(1, 12).Select(index => new KeyDefinition((Keys)((int)Keys.F1 + index - 1), $"F{index}", 1)),
    ];

    private static KeyDefinition[] NumberRow() =>
    [
        new(Keys.Oemtilde, "`", 1),
        .. Enumerable.Range(0, 10).Select(index => new KeyDefinition((Keys)((int)Keys.D0 + index), index.ToString(), 1)),
        new(Keys.OemMinus, "-", 1),
        new(Keys.Oemplus, "=", 1),
        new(Keys.Back, "Backspace", 2),
    ];

    private static KeyDefinition[] TopLetterRow() =>
    [
        new(Keys.Tab, "Tab", 1.5f),
        .. "QWERTYUIOP".Select(letter => new KeyDefinition((Keys)Enum.Parse(typeof(Keys), letter.ToString()), letter.ToString(), 1)),
        new(Keys.OemOpenBrackets, "[", 1),
        new(Keys.OemCloseBrackets, "]", 1),
        new(Keys.OemPipe, "\\", 1.5f),
    ];

    private static KeyDefinition[] HomeLetterRow() =>
    [
        new(Keys.CapsLock, "Caps", 1.8f),
        .. "ASDFGHJKL".Select(letter => new KeyDefinition((Keys)Enum.Parse(typeof(Keys), letter.ToString()), letter.ToString(), 1)),
        new(Keys.OemSemicolon, ";", 1),
        new(Keys.OemQuotes, "'", 1),
        new(Keys.Enter, "Enter", 2.2f),
    ];

    private static KeyDefinition[] BottomLetterRow() =>
    [
        new(Keys.LShiftKey, "Shift", 2.3f),
        .. "ZXCVBNM".Select(letter => new KeyDefinition((Keys)Enum.Parse(typeof(Keys), letter.ToString()), letter.ToString(), 1)),
        new(Keys.Oemcomma, ",", 1),
        new(Keys.OemPeriod, ".", 1),
        new(Keys.OemQuestion, "/", 1),
        new(Keys.RShiftKey, "Shift", 2.3f),
    ];

    private static KeyDefinition[] ModifierRow() =>
    [
        new(Keys.LControlKey, "Ctrl", 1.5f),
        new(Keys.LWin, "Win", 1.5f),
        new(Keys.LMenu, "Alt", 1.5f),
        new(Keys.Space, "Space", 6),
        new(Keys.RMenu, "Alt", 1.5f),
        new(Keys.RWin, "Win", 1.5f),
        new(Keys.Apps, "Menu", 1.5f),
        new(Keys.RControlKey, "Ctrl", 1.5f),
    ];

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

    private void UpdateStatus()
    {
        _progress.Text = _t["keyboardTest.progress", _tested.Count, _keyLabels.Count];
        _pressedLabel.Text = _t["keyboardTest.pressed", _pressed.Count == 0 ? "—" : string.Join(" + ", _pressed.Select(key => _keyLabels[key].Text))];
    }
}
