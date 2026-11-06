using System.Globalization;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Groups the subjective peripheral checks in one window. Display, keyboard, mouse and touchpad results need a person
/// to confirm them; only the gamepad state is read automatically. Nothing is written to the hardware.
/// </summary>
internal sealed class PeripheralTestForm : Form
{
    private readonly Translator _t;
    private readonly HardwareInventory _hardware;
    private readonly CheckedListBox _keys = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly Label _keyCount = Ui.Label();
    private readonly Label _mouseStatus = Ui.Label();
    private readonly Label _gamepadStatus = Ui.Label();
    private readonly Label _gamepadButtons = Ui.Label();
    private readonly Label _gamepadTriggers = Ui.Label();
    private readonly Label _gamepadSticks = Ui.Label();
    private readonly Label _displayStatus = Ui.Label();
    private readonly Label _speakerStatus = Ui.Label();
    private readonly Label _microphoneStatus = Ui.Label();
    private readonly System.Windows.Forms.Timer _gamepadTimer = new() { Interval = 50 };
    private AudioCheckResult _audio;
    private int _leftClicks;
    private int _rightClicks;
    private int _middleClicks;
    private int _x1Clicks;
    private int _x2Clicks;
    private int _wheelDelta;

    public PeripheralTestForm(Translator t, HardwareInventory hardware, AudioCheckResult audio, Font font)
    {
        _t = t;
        _hardware = hardware;
        _audio = audio;
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["peripheralTest.title"];
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(820), LogicalToDeviceUnits(620));
        MinimumSize = new Size(LogicalToDeviceUnits(700), LogicalToDeviceUnits(520));
        KeyPreview = true;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildAudioPage());
        tabs.TabPages.Add(BuildDisplayPage());
        tabs.TabPages.Add(BuildKeyboardPage());
        tabs.TabPages.Add(BuildMousePage());
        tabs.TabPages.Add(BuildGamepadPage());

        var close = Ui.Button();
        close.Text = t["ui.ok"];
        close.DialogResult = DialogResult.OK;
        close.Anchor = AnchorStyles.Right;
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, Ui.SPACING, 0, 0) };
        buttons.Controls.Add(close);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.SPACING) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;

        KeyDown += OnKeyDown;
        _gamepadTimer.Tick += (_, _) => PollGamepad();
        Shown += (_, _) => _gamepadTimer.Start();
        FormClosed += (_, _) => _gamepadTimer.Stop();
        Ui.ScaleButtons(this);
        BuildKeyList();
        UpdateAudioLabels();
    }

    public AudioCheckResult AudioResult => _audio;

    private TabPage BuildAudioPage()
    {
        var page = new TabPage(_t["peripheralTest.audio"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var speaker = _hardware.Peripherals?.FirstOrDefault(item => item.Kind == PeripheralKind.Speaker)?.Name;
        var microphone = _hardware.Peripherals?.FirstOrDefault(item => item.Kind == PeripheralKind.Microphone)?.Name;

        var speakerButton = Ui.Button();
        speakerButton.Text = _t["ui.speakerTest"];
        speakerButton.Enabled = speaker is not null;
        speakerButton.Click += async (_, _) => await RunSpeakerTestAsync(speakerButton);

        var microphoneButton = Ui.Button();
        microphoneButton.Text = _t["ui.micTest"];
        microphoneButton.Enabled = microphone is not null;
        microphoneButton.Click += async (_, _) => await RunMicrophoneTestAsync(microphoneButton);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddRow(layout, 0, _t["ui.speakerTest"], speaker ?? _t["peripheralTest.notDetected"]);
        AddRow(layout, 1, _t["ui.micTest"], microphone ?? _t["peripheralTest.notDetected"]);
        layout.Controls.Add(speakerButton, 0, 2);
        layout.Controls.Add(microphoneButton, 0, 3);
        AddRow(layout, 4, _t["advanced.status"], "");
        layout.Controls.Add(_speakerStatus, 0, 5);
        layout.Controls.Add(_microphoneStatus, 0, 6);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildDisplayPage()
    {
        var page = new TabPage(_t["peripheralTest.display"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var screens = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, Ui.SPACING) };
        foreach (var screen in Screen.AllScreens)
        {
            screens.Items.Add($"{screen.DeviceName}  {screen.Bounds.Width} × {screen.Bounds.Height}");
        }

        if (screens.Items.Count > 0)
        {
            screens.SelectedIndex = 0;
        }

        var start = Ui.Button();
        start.Text = _t["peripheralTest.startDisplay"];
        start.Click += (_, _) =>
        {
            if (screens.SelectedIndex < 0)
            {
                return;
            }

            using var form = new DisplayTestForm(_t, Screen.AllScreens[screens.SelectedIndex]);
            form.ShowDialog(this);
            var answer = MessageBox.Show(this, _t["peripheralTest.displayPrompt"], _t["peripheralTest.display"], MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            _displayStatus.Text = answer == DialogResult.Yes ? _t["peripheralTest.displayNormal"] : _t["peripheralTest.displayAbnormal"];
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddRow(layout, 0, _t["peripheralTest.screen"], "");
        layout.Controls.Add(screens, 0, 1);
        layout.Controls.Add(start, 0, 2);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(740), 0), Text = _t["peripheralTest.displayHelp"], Margin = new Padding(0, Ui.SPACING, 0, Ui.SPACING) }, 0, 3);
        layout.Controls.Add(_displayStatus, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildKeyboardPage()
    {
        var page = new TabPage(_t["peripheralTest.keyboard"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var hint = new Label { AutoSize = true, Text = _t["peripheralTest.pressKeys"], Margin = new Padding(0, 0, 0, Ui.SPACING) };
        var clear = Ui.Button();
        clear.Text = _t["peripheralTest.clear"];
        clear.Click += (_, _) =>
        {
            for (var index = 0; index < _keys.Items.Count; index++)
            {
                _keys.SetItemChecked(index, false);
            }

            _keyCount.Text = _t["peripheralTest.keysTested", 0];
        };

        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0) };
        top.Controls.Add(clear);
        top.Controls.Add(_keyCount);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_keys, 0, 1);
        layout.Controls.Add(top, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildMousePage()
    {
        var page = new TabPage(_t["peripheralTest.mouse"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var panel = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(220), BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window, TabStop = true };
        panel.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _leftClicks++;
            }
            else if (e.Button == MouseButtons.Right)
            {
                _rightClicks++;
            }
            else if (e.Button == MouseButtons.Middle)
            {
                _middleClicks++;
            }
            else if (e.Button == MouseButtons.XButton1)
            {
                _x1Clicks++;
            }
            else if (e.Button == MouseButtons.XButton2)
            {
                _x2Clicks++;
            }

            UpdateMouseStatus();
        };
        panel.MouseWheel += (_, e) =>
        {
            _wheelDelta += e.Delta;
            UpdateMouseStatus();
        };
        panel.MouseMove += (_, e) => _mouseStatus.Text = $"{_t["peripheralTest.pointer"]}: {e.X}, {e.Y}";

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(220)));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Text = _t["peripheralTest.mouseHelp"], MaximumSize = new Size(LogicalToDeviceUnits(740), 0), Margin = new Padding(0, 0, 0, Ui.SPACING) }, 0, 0);
        layout.Controls.Add(panel, 0, 1);
        layout.Controls.Add(_mouseStatus, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildGamepadPage()
    {
        var page = new TabPage(_t["peripheralTest.gamepad"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.Controls.Add(new Label { AutoSize = true, Text = _t["peripheralTest.gamepadHelp"], MaximumSize = new Size(LogicalToDeviceUnits(740), 0), Margin = new Padding(0, 0, 0, Ui.SPACING) }, 0, 0);
        layout.Controls.Add(_gamepadStatus, 0, 1);
        AddRow(layout, 2, _t["peripheralTest.buttons"], "");
        layout.Controls.Add(_gamepadButtons, 0, 3);
        AddRow(layout, 4, _t["peripheralTest.triggers"], "");
        layout.Controls.Add(_gamepadTriggers, 0, 5);
        layout.Controls.Add(_gamepadSticks, 0, 6);
        page.Controls.Add(layout);
        return page;
    }

    private void AddRow(TableLayoutPanel layout, int row, string label, string value)
    {
        var line = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(740), 0),
            Text = string.IsNullOrEmpty(value) ? label : $"{label}: {value}",
            Margin = new Padding(0, 0, 0, Ui.SPACING / 2),
        };
        layout.Controls.Add(line, 0, row);
    }

    private void BuildKeyList()
    {
        var keys = new List<Keys>();
        keys.AddRange(Enumerable.Range((int)Keys.A, 26).Select(value => (Keys)value));
        keys.AddRange(Enumerable.Range((int)Keys.D0, 10).Select(value => (Keys)value));
        keys.AddRange(Enumerable.Range((int)Keys.F1, 12).Select(value => (Keys)value));
        keys.AddRange([Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Space, Keys.Enter, Keys.Back, Keys.Tab, Keys.Escape, Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey, Keys.LMenu, Keys.RMenu]);
        keys.AddRange([Keys.OemSemicolon, Keys.OemQuestion, Keys.Oemtilde, Keys.OemOpenBrackets, Keys.OemPipe, Keys.OemCloseBrackets, Keys.OemQuotes, Keys.OemBackslash, Keys.Oemcomma, Keys.OemPeriod, Keys.OemMinus, Keys.Oemplus]);
        keys.AddRange([Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9, Keys.Add, Keys.Subtract, Keys.Multiply, Keys.Divide, Keys.Decimal, Keys.NumLock]);
        keys.AddRange([Keys.CapsLock, Keys.PrintScreen, Keys.Scroll, Keys.Pause, Keys.LWin, Keys.RWin, Keys.Apps, Keys.Sleep]);
        foreach (var key in keys.Distinct())
        {
            _keys.Items.Add(key);
        }

        _keyCount.Text = _t["peripheralTest.keysTested", 0];
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            return;
        }

        for (var index = 0; index < _keys.Items.Count; index++)
        {
            if (_keys.Items[index] is Keys key && key == e.KeyCode)
            {
                _keys.SetItemChecked(index, true);
                break;
            }
        }

        var checkedCount = _keys.CheckedItems.Count;
        _keyCount.Text = _t["peripheralTest.keysTested", checkedCount];
    }

    private void UpdateMouseStatus()
    {
        _mouseStatus.Text = _t["peripheralTest.mouseCounts", _leftClicks, _rightClicks, _middleClicks, _x1Clicks, _x2Clicks, _wheelDelta];
    }

    private async Task RunSpeakerTestAsync(Button button)
    {
        button.Enabled = false;
        try
        {
            await Task.Run(AudioCheck.PlayTone);
            var heard = MessageBox.Show(this, _t["ui.speakerConfirm"], _t["ui.speakerTest"], MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            _audio = _audio with { SpeakerHeard = heard };
            UpdateAudioLabels();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, _t["peripheralTest.audioFailed", exception.Message], _t["ui.speakerTest"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task RunMicrophoneTestAsync(Button button)
    {
        button.Enabled = false;
        try
        {
            _microphoneStatus.Text = _t["ui.micRecording"];
            await Task.Run(() => AudioCheck.RecordAndPlayBack(() => { }));
            var heard = MessageBox.Show(this, _t["ui.micConfirm"], _t["ui.micTest"], MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            _audio = _audio with { MicrophoneHeard = heard };
            UpdateAudioLabels();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, _t["peripheralTest.audioFailed", exception.Message], _t["ui.micTest"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private void UpdateAudioLabels()
    {
        _speakerStatus.Text = _audio.SpeakerHeard switch
        {
            true => _t["audioCheck.heard"],
            false => _t["audioCheck.notHeard"],
            _ => _t["peripheralTest.notTested"],
        };
        _microphoneStatus.Text = _audio.MicrophoneHeard switch
        {
            true => _t["audioCheck.heard"],
            false => _t["audioCheck.notHeard"],
            _ => _t["peripheralTest.notTested"],
        };
    }

    private void PollGamepad()
    {
        try
        {
            var result = XInputGetState(0, out var state);
            if (result != 0)
            {
                _gamepadStatus.Text = _t["peripheralTest.gamepadMissing"];
                _gamepadButtons.Text = "";
                _gamepadTriggers.Text = "";
                _gamepadSticks.Text = "";
                return;
            }

            _gamepadStatus.Text = _t["peripheralTest.gamepadConnected"];
            _gamepadButtons.Text = DescribeButtons(state.Gamepad.Buttons);
            _gamepadTriggers.Text = $"{state.Gamepad.LeftTrigger} / {state.Gamepad.RightTrigger}";
            _gamepadSticks.Text = $"L {state.Gamepad.LeftThumbX}, {state.Gamepad.LeftThumbY}  R {state.Gamepad.RightThumbX}, {state.Gamepad.RightThumbY}";
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _gamepadTimer.Stop();
            _gamepadStatus.Text = _t["peripheralTest.gamepadUnavailable"];
        }
    }

    private static string DescribeButtons(ushort buttons)
    {
        if (buttons == 0)
        {
            return "";
        }

        var pressed = new List<string>();
        Add(0x1000, "A");
        Add(0x2000, "B");
        Add(0x4000, "X");
        Add(0x8000, "Y");
        Add(0x0001, "Up");
        Add(0x0002, "Down");
        Add(0x0004, "Left");
        Add(0x0008, "Right");
        Add(0x0010, "Start");
        Add(0x0020, "Back");
        Add(0x0040, "L3");
        Add(0x0080, "R3");
        Add(0x0100, "LB");
        Add(0x0200, "RB");
        return string.Join(", ", pressed);

        void Add(ushort mask, string name)
        {
            if ((buttons & mask) != 0)
            {
                pressed.Add(name);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint index, out XInputState state);
}
