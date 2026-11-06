using System.Globalization;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Groups the subjective peripheral checks in one window. Display, keyboard and mouse results need a person to confirm
/// them; only the gamepad state is read automatically. Nothing is written to the hardware.
/// </summary>
internal sealed class PeripheralTestForm : Form
{
    private readonly Translator _t;
    private readonly HardwareInventory _hardware;
    private readonly KeyboardTestView _keyboard;
    private readonly MouseTestView _mouse;
    private readonly CameraTestView _camera;
    private readonly TabControl _tabs;
    private readonly Label _gamepadStatus = Ui.Label();
    private readonly Label _gamepadButtons = Ui.Label();
    private readonly Label _gamepadTriggers = Ui.Label();
    private readonly Label _gamepadSticks = Ui.Label();
    private readonly Label _displayStatus = Ui.Label();
    private readonly Label _speakerStatus = Ui.Label();
    private readonly Label _microphoneStatus = Ui.Label();
    private readonly System.Windows.Forms.Timer _gamepadTimer = new() { Interval = 50 };
    private AudioCheckResult _audio;

    public PeripheralTestForm(Translator t, HardwareInventory hardware, AudioCheckResult audio, Font font)
    {
        _t = t;
        _hardware = hardware;
        _audio = audio;
        _keyboard = new KeyboardTestView(t, font);
        _mouse = new MouseTestView(t, font);
        _camera = new CameraTestView(t, font);
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["peripheralTest.title"];
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(820), LogicalToDeviceUnits(620));
        MinimumSize = new Size(LogicalToDeviceUnits(700), LogicalToDeviceUnits(520));
        KeyPreview = true;

        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildAudioPage());
        _tabs.TabPages.Add(BuildDisplayPage());
        _tabs.TabPages.Add(BuildKeyboardPage());
        _tabs.TabPages.Add(BuildMousePage());
        _tabs.TabPages.Add(BuildCameraPage());
        _tabs.TabPages.Add(BuildGamepadPage());

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
        root.Controls.Add(_tabs, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;

        KeyDown += OnKeyDown;
        KeyUp += (_, e) => _keyboard.MarkKeyUp(e.KeyCode);
        _gamepadTimer.Tick += (_, _) => PollGamepad();
        Shown += (_, _) => _gamepadTimer.Start();
        Ui.ScaleButtons(this);
        UpdateAudioLabels();
    }

    public AudioCheckResult AudioResult => _audio;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gamepadTimer.Stop();
            _gamepadTimer.Dispose();
        }

        base.Dispose(disposing);
    }

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

        var leftButton = Ui.Button();
        leftButton.Text = _t["audioCheck.left"];
        leftButton.Enabled = speaker is not null;
        leftButton.Click += async (_, _) => await PlayChannelAsync(leftButton, true, false);
        var rightButton = Ui.Button();
        rightButton.Text = _t["audioCheck.right"];
        rightButton.Enabled = speaker is not null;
        rightButton.Click += async (_, _) => await PlayChannelAsync(rightButton, false, true);
        var stereoButton = Ui.Button();
        stereoButton.Text = _t["audioCheck.stereo"];
        stereoButton.Enabled = speaker is not null;
        stereoButton.Click += async (_, _) => await PlayChannelAsync(stereoButton, true, true);
        var channels = Ui.Flow();
        channels.Controls.AddRange([leftButton, rightButton, stereoButton]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddRow(layout, 0, _t["ui.speakerTest"], speaker ?? _t["peripheralTest.notDetected"]);
        AddRow(layout, 1, _t["ui.micTest"], microphone ?? _t["peripheralTest.notDetected"]);
        layout.Controls.Add(speakerButton, 0, 2);
        layout.Controls.Add(microphoneButton, 0, 3);
        layout.Controls.Add(channels, 0, 4);
        AddRow(layout, 5, _t["advanced.status"], "");
        layout.Controls.Add(_speakerStatus, 0, 6);
        layout.Controls.Add(_microphoneStatus, 0, 7);
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

    private TabPage BuildKeyboardPage() =>
        new(_t["peripheralTest.keyboard"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true, Controls = { _keyboard } };

    private TabPage BuildMousePage() =>
        new(_t["peripheralTest.mouse"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true, Controls = { _mouse } };

    private TabPage BuildCameraPage() =>
        new(_t["peripheralTest.camera"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true, Controls = { _camera } };

    private TabPage BuildGamepadPage()
    {
        var page = new TabPage(_t["peripheralTest.gamepad"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = new Padding(0) };
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
        var vibrate = Ui.Button();
        vibrate.Text = _t["peripheralTest.vibrate"];
        vibrate.Click += async (_, _) => await TestVibrationAsync(vibrate);
        layout.Controls.Add(vibrate, 0, 7);
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && _tabs.SelectedIndex != 2)
        {
            Close();
            return;
        }

        _keyboard.MarkKeyDown(e.KeyCode);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var keyboard = new RawInputDevice
        {
            UsagePage = 1,
            Usage = 6,
            Flags = 0x00000100,
            Target = Handle,
        };
        _ = RegisterRawInputDevices([keyboard], 1, Marshal.SizeOf<RawInputDevice>());
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x00FF && message.LParam != IntPtr.Zero)
        {
            var size = 0;
            _ = GetRawInputData(message.LParam, 0x10000003, IntPtr.Zero, ref size, Marshal.SizeOf<RawInputHeader>());
            if (size > 0)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetRawInputData(message.LParam, 0x10000003, buffer, ref size, Marshal.SizeOf<RawInputHeader>()) == size &&
                        Marshal.ReadInt32(buffer) == 1)
                    {
                        var keyboard = Marshal.PtrToStructure<RawKeyboard>(buffer + Marshal.SizeOf<RawInputHeader>());
                        if (keyboard.Message is 0x0100 or 0x0104 && keyboard.VirtualKey is > 0 and < 0x100)
                        {
                            _keyboard.MarkKeyDown((Keys)keyboard.VirtualKey);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        base.WndProc(ref message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public int Type;
        public int Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref int size, int headerSize);

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

    private async Task PlayChannelAsync(Button button, bool left, bool right)
    {
        button.Enabled = false;
        try
        {
            await Task.Run(() => AudioCheck.PlayChannel(left, right));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, _t["peripheralTest.audioFailed", exception.Message], _t["peripheralTest.audio"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private async Task TestVibrationAsync(Button button)
    {
        button.Enabled = false;
        try
        {
            var vibration = new XInputVibration { LeftMotorSpeed = ushort.MaxValue, RightMotorSpeed = ushort.MaxValue };
            if (XInputSetState(0, ref vibration) != 0)
            {
                return;
            }

            await Task.Delay(800);
            vibration = default;
            _ = XInputSetState(0, ref vibration);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _gamepadStatus.Text = _t["peripheralTest.gamepadUnavailable"];
        }
        finally
        {
            button.Enabled = true;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint index, out XInputState state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputSetState(uint index, ref XInputVibration vibration);
}
