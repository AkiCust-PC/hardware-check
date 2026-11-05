using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AkikazuHardwareCheck.Core.Configuration;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;
using AkikazuHardwareCheck.Core.Reporting;
using AkikazuHardwareCheck.Core.Testing;

namespace AkikazuHardwareCheck.App;

internal sealed class MainForm : Form
{
    private const int PROGRESS_MAX = Ui.PERCENT_MAX;
    private const double PERCENT = 100;

    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1);

    private static readonly (string Code, string Name)[] LanguageChoices =
    [
        (AppLanguage.JAPANESE, "日本語"),
        (AppLanguage.SIMPLIFIED_CHINESE, "简体中文"),
        (AppLanguage.TRADITIONAL_CHINESE, "繁體中文"),
        (AppLanguage.ENGLISH, "English"),
    ];

    private const int MONITOR_SECTION_HEIGHT = 205;
    private const int CONTROL_SECTION_HEIGHT = 200;

    // Without the graphics card selector row.
    private const int CONTROL_SECTION_COMPACT_HEIGHT = 162;
    private const int CLOCK_INTERVAL_MS = 1000;

    // shutdown.exe returns at once after scheduling the restart, so this only waits for it to report a refusal.
    private const int FIRMWARE_RESTART_WAIT_MS = 5000;
    private const int WM_COMMAND = 0x0111;
    private const int MENU_ID_MASK = 0xFFFF;
    private const int PRIMARY_BUTTON_HEIGHT = 36;

    private readonly TableLayoutPanel _noticePanel = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, BackColor = SystemColors.Info, Padding = new Padding(Ui.SPACING), Margin = new Padding(0, 0, 0, Ui.SPACING), Visible = false };
    private readonly Label _noticeLabel = Ui.Label();
    private readonly Button _elevateButton = Ui.Button();

    private readonly InfoView _hardwareList = new();
    private readonly RadioButton _viewFullButton = new() { AutoSize = true, Margin = new Padding(0, 0, Ui.SPACING * 2, 0) };
    private readonly RadioButton _viewModelsButton = new() { AutoSize = true, Margin = new Padding(0) };

    private readonly Label _phaseLabel = Ui.Label();
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill, Maximum = PROGRESS_MAX, Margin = new Padding(0, 0, 0, Ui.SPACING) };
    private readonly MetricTile _cpuTemperatureTile = new();
    private readonly MetricTile _cpuLoadTile = new();
    private readonly MetricTile _gpuTemperatureTile = new();
    private readonly MetricTile _gpuLoadTile = new();
    private readonly MetricTile _memoryLoadTile = new();
    private readonly MetricTile _diskLoadTile = new();
    private readonly MetricTile _uploadTile = new();
    private readonly MetricTile _downloadTile = new();

    private readonly LinkLabel _copyrightLink = new() { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(Ui.SPACING, Ui.SPACING / 2, 0, Ui.SPACING / 2) };
    private readonly TableLayoutPanel _targetPanel = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0), Visible = false };
    private readonly TableLayoutPanel _rightPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
    private readonly Label _targetLabel = Ui.Label();
    private readonly ComboBox _targetBox = Ui.Combo();
    private readonly Button _startButton = Ui.Button();
    private readonly Button _gpuOnlyButton = Ui.Button();
    private readonly Button _cpuOnlyButton = Ui.Button();
    private readonly Button _memoryTestButton = Ui.Button();
    private readonly Button _diskTestButton = Ui.Button();
    private readonly Button _diskHealthButton = Ui.Button();
    private readonly Button _cpuInfoButton = Ui.Button();
    private readonly Button _speakerTestButton = Ui.Button();
    private readonly Button _micTestButton = Ui.Button();

    private readonly Label _overallLabel = Ui.Label(isBold: true);
    private readonly InfoView _resultList = new();
    private readonly Button _detailsButton = Ui.Button();

    private readonly Label _statusLabel = Ui.Label();

    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = CLOCK_INTERVAL_MS };
    private NativeMenu? _menu;
    private GroupBox? _hardwareGroup;
    private GroupBox? _monitorGroup;
    private GroupBox? _controlGroup;
    private GroupBox? _resultGroup;
    private bool _isApplyingView;
    private bool _isDriverOurs;
    private bool _isDriverPackageOurs;
    private AudioCheckResult _audioCheck = new(null, null);

    private AppSettings _settings = AppSettings.Load(AppSettings.DefaultSettingsPath);
    private Translator _t;
    private HardwareInventory? _hardware;
    private IReadOnlyList<SensorReading> _sensors = [];
    private LhmSensorSource? _sensorSource;
    private SensorSampler? _liveSampler;
    private ActivityMonitor? _activityMonitor;
    private CancellationTokenSource? _testStop;
    private TestRunResult? _lastRun;
    private MemoryTestResult? _memoryResult;
    private DiskTestResult? _diskResult;
    private SavedReport? _lastReport;
    private string? _statusText;
    private bool _closeAfterTest;

    public MainForm()
    {
        _t = Translator.Load(_settings.Language is { } saved && AppLanguage.Parse(saved) is { } language
            ? language
            : AppLanguage.FromCulture(CultureInfo.CurrentUICulture.Name));

        Controls.Add(BuildRootPanel());
        Ui.ScaleButtons(this);
        ScaleLayout();

        Icon = Ui.AppIcon;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(LogicalToDeviceUnits(1180), LogicalToDeviceUnits(800));
        MinimumSize = new Size(LogicalToDeviceUnits(900), LogicalToDeviceUnits(720));

        WireEvents();
        ApplyLanguage();
        UpdateCommandStates();
    }

    private bool IsTestRunning => _testStop is not null;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = LoadHardwareAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Windows frees the menu with the window, so a new window handle needs a new menu.
        _menu = null;
        BuildMenu();
        UpdateCommandStates();
    }

    protected override void WndProc(ref Message m)
    {
        // Menus report through WM_COMMAND with no control handle; messages from child controls keep their normal path.
        if (m.Msg == WM_COMMAND && m.LParam == IntPtr.Zero && (m.WParam.ToInt64() >> 16) == 0)
        {
            RunMenuCommand((int)(m.WParam.ToInt64() & MENU_ID_MASK));
            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                StartIfAllowed(_startButton, () =>
                {
                    if (!IsTestRunning)
                    {
                        _ = RunTestAsync(runIdle: true, runGpu: true, runCpu: true);
                    }
                });
                return true;
            case Keys.F7:
                GenerateReport();
                return true;
            case Keys.Escape when IsTestRunning:
                if (MessageBox.Show(this, _t["ui.confirmStop"], _t["ui.confirmTitle"], MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    RequestStop();
                }

                return true;
            case Keys.Control | Keys.C when ActiveControl is not TextBoxBase:
                CopyConfiguration();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (IsTestRunning && !_closeAfterTest)
        {
            e.Cancel = true;
            if (MessageBox.Show(this, _t["ui.confirmCloseRunning"], _t["ui.confirmTitle"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _closeAfterTest = true;
                RequestStop();
            }

            return;
        }

        if (IsTestRunning)
        {
            e.Cancel = true;
            return;
        }

        _clockTimer.Dispose();
        StopLiveMonitor();
        _activityMonitor?.Dispose();
        _sensorSource?.Dispose();
        if (_isDriverOurs)
        {
            // Only a driver this run installed is removed. This process keeps the driver open until it ends, so a
            // copy of the program does the removal after it, with this process's administrator rights.
            StartDriverRemoval();
        }

        base.OnFormClosing(e);
    }

    private static string? Number(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Format(double? value, string format, string unit) =>
        value is { } number ? $"{number.ToString(format, CultureInfo.InvariantCulture)} {unit}" : "—";

    private static void StartIfAllowed(Button button, Action action)
    {
        if (button.Enabled)
        {
            action();
        }
    }

    private TableLayoutPanel BuildRootPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.SPACING * 2) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildNotice(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        return root;
    }

    private TableLayoutPanel BuildFooter()
    {
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_statusLabel, 0, 0);
        footer.Controls.Add(_copyrightLink, 1, 0);
        return footer;
    }

    private TableLayoutPanel BuildNotice()
    {
        _noticePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _noticePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // The system info text color is too dim on the dark info background.
        _noticeLabel.ForeColor = Application.IsDarkModeEnabled ? SystemColors.ControlText : SystemColors.InfoText;
        _noticePanel.Controls.Add(_noticeLabel, 0, 0);
        _elevateButton.Anchor = AnchorStyles.Right;
        _elevateButton.Margin = new Padding(Ui.SPACING, 0, 0, 0);
        _noticePanel.Controls.Add(_elevateButton, 1, 0);
        return _noticePanel;
    }

    private TableLayoutPanel BuildBody()
    {
        var viewSwitch = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, Ui.SPACING) };
        viewSwitch.Controls.AddRange([_viewFullButton, _viewModelsButton]);
        _hardwareGroup = Ui.Section(viewSwitch, _hardwareList);

        _phaseLabel.Margin = new Padding(0, 0, 0, Ui.SPACING / 2);
        _monitorGroup = Ui.Section(_phaseLabel, BuildMonitorPanel());
        _controlGroup = Ui.Section(null, BuildControlPanel());
        _detailsButton.Margin = new Padding(0, 0, 0, Ui.SPACING / 2);
        _resultGroup = Ui.Section(_detailsButton, BuildResultPanel());

        _rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(MONITOR_SECTION_HEIGHT)));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(CONTROL_SECTION_COMPACT_HEIGHT)));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var (group, row) in new[] { (_monitorGroup, 0), (_controlGroup, 1), (_resultGroup, 2) })
        {
            group.Margin = new Padding(0, 0, 0, Ui.SPACING);
            _rightPanel.Controls.Add(group, 0, row);
        }

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(_hardwareGroup, 0, 0);
        body.Controls.Add(_rightPanel, 1, 0);
        return body;
    }

    private TableLayoutPanel BuildMonitorPanel()
    {
        var panel = new TableLayoutPanel { ColumnCount = 4, RowCount = 3, Margin = new Padding(0) };
        for (var column = 0; column < panel.ColumnCount; column++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _progressBar.Height = LogicalToDeviceUnits(Ui.SPACING);
        panel.Controls.Add(_progressBar, 0, 0);
        panel.SetColumnSpan(_progressBar, 4);

        MetricTile[][] rows =
        [
            [_cpuTemperatureTile, _cpuLoadTile, _gpuTemperatureTile, _gpuLoadTile],
            [_memoryLoadTile, _diskLoadTile, _uploadTile, _downloadTile],
        ];
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                panel.Controls.Add(rows[row][column], column, row + 1);
            }
        }

        return panel;
    }

    private TableLayoutPanel BuildControlPanel()
    {
        _targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _targetPanel.Controls.Add(_targetLabel, 0, 0);
        _targetBox.Dock = DockStyle.Fill;
        _targetBox.Margin = new Padding(0);
        _targetPanel.Controls.Add(_targetBox, 1, 0);

        _startButton.AutoSize = false;
        _startButton.Dock = DockStyle.Fill;
        _startButton.MinimumSize = new Size(0, Ui.BUTTON_MIN_HEIGHT);
        _startButton.Margin = new Padding(0, Ui.SPACING, 0, Ui.SPACING);

        // Windows draws this as the window's default button, without Enter being able to start a test.
        _startButton.NotifyDefault(true);

        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(PRIMARY_BUTTON_HEIGHT + (Ui.SPACING * 2))));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(_targetPanel, 0, 0);
        panel.Controls.Add(_startButton, 0, 1);
        panel.Controls.Add(Ui.ButtonGrid(4, _gpuOnlyButton, _cpuOnlyButton, _memoryTestButton, _diskTestButton, _diskHealthButton, _speakerTestButton, _micTestButton, _cpuInfoButton), 0, 2);
        return panel;
    }

    private TableLayoutPanel BuildResultPanel()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(_overallLabel, 0, 0);
        _resultList.Dock = DockStyle.Fill;
        panel.Controls.Add(_resultList, 0, 1);
        return panel;
    }

    /// <summary>The start button sits in a taller row than the shared button height.</summary>
    private void ScaleLayout()
    {
        _startButton.MinimumSize = new Size(0, LogicalToDeviceUnits(PRIMARY_BUTTON_HEIGHT));
    }
    private void WireEvents()
    {
        _copyrightLink.LinkClicked += (_, _) => OpenInShell(_t["ui.downloadPageUrl"]);
        _viewFullButton.CheckedChanged += (_, _) => OnViewChanged();
        _elevateButton.Click += (_, _) => RestartElevated();
        _startButton.Click += (_, _) =>
        {
            if (IsTestRunning)
            {
                RequestStop();
            }
            else
            {
                _ = RunTestAsync(runIdle: true, runGpu: true, runCpu: true);
            }
        };
        _gpuOnlyButton.Click += (_, _) => _ = RunTestAsync(runIdle: false, runGpu: true, runCpu: false);
        _cpuOnlyButton.Click += (_, _) => _ = RunTestAsync(runIdle: false, runGpu: false, runCpu: true);
        _detailsButton.Click += (_, _) => ShowDetails();
        _diskHealthButton.Click += (_, _) => ShowDiskHealth();
        _cpuInfoButton.Click += (_, _) => ShowCpuInfo();
        _memoryTestButton.Click += (_, _) => _ = RunMemoryTestAsync();
        _diskTestButton.Click += (_, _) => _ = RunDiskTestAsync();
        _speakerTestButton.Click += (_, _) => _ = RunSpeakerTestAsync();
        _micTestButton.Click += (_, _) => _ = RunMicrophoneTestAsync();
        Resize += (_, _) => FitMessages();
        _clockTimer.Tick += (_, _) => _hardwareList.UpdateValue(_t["summary.time"], HardwareSummary.TimeText(DateTimeOffset.Now, TimeZoneInfo.Local));
    }

    private void StartDriverRemoval()
    {
        if (Environment.ProcessPath is not { } path)
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, [Program.REMOVE_DRIVER_ARGUMENT, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), _isDriverPackageOurs.ToString(), _t.Language]) { UseShellExecute = false });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(_t["ui.driverRemoveFailed"], _t["ui.appName"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Trace.TraceWarning(exception.Message);
        }
    }

    private void BuildMenu()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var menu = new NativeMenu();
        var file = menu.AddSubmenu(_t["ui.menuFile"]);
        NativeMenu.AddItem(file, MenuCommand.Report, $"{_t["ui.report"]}\tF7");
        NativeMenu.AddItem(file, MenuCommand.Copy, $"{_t["ui.copy"]}\tCtrl+C");
        NativeMenu.AddSeparator(file);
        NativeMenu.AddItem(file, MenuCommand.OpenReport, _t["ui.openReport"]);
        NativeMenu.AddItem(file, MenuCommand.OpenFolder, _t["ui.openFolder"]);
        NativeMenu.AddSeparator(file);
        NativeMenu.AddItem(file, MenuCommand.RestartToFirmware, _t["ui.restartToFirmware"]);
        NativeMenu.AddItem(file, MenuCommand.Settings, _t["ui.settings"]);

        var language = menu.AddSubmenu(_t["ui.menuLanguage"]);
        for (var index = 0; index < LanguageChoices.Length; index++)
        {
            NativeMenu.AddItem(language, MenuCommand.FirstLanguage + index, LanguageChoices[index].Name);
        }

        menu.SetRadio(MenuCommand.FirstLanguage, LanguageChoices.Length, MenuCommand.FirstLanguage + Array.FindIndex(LanguageChoices, choice => choice.Code == _t.Language));
        menu.Attach(Handle, _menu);
        _menu = menu;
    }

    private void RunMenuCommand(int id)
    {
        var languageIndex = id - (int)MenuCommand.FirstLanguage;
        if (languageIndex >= 0 && languageIndex < LanguageChoices.Length)
        {
            OnLanguageChosen(LanguageChoices[languageIndex].Code);
            return;
        }

        switch ((MenuCommand)id)
        {
            case MenuCommand.Report:
                GenerateReport();
                break;
            case MenuCommand.Copy:
                CopyConfiguration();
                break;
            case MenuCommand.OpenReport:
                OpenInShell(_lastReport?.HtmlPath);
                break;
            case MenuCommand.OpenFolder:
                OpenReportFolder();
                break;
            case MenuCommand.RestartToFirmware:
                RestartToFirmware();
                break;
            case MenuCommand.Settings:
                OpenSettingsForm();
                break;
        }
    }

    /// <summary>
    /// Secure Boot can only be switched inside the firmware: Windows has no general interface to turn it on, by design.
    /// This is the closest thing to one click, so it restarts straight into the firmware screen after showing what to change.
    /// </summary>
    private void RestartToFirmware()
    {
        if (MessageBox.Show(this, _t["ui.restartToFirmwareConfirm"], _t["ui.restartToFirmware"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 0") { UseShellExecute = false, CreateNoWindow = true });
            if (process is not null && process.WaitForExit(FIRMWARE_RESTART_WAIT_MS) && process.ExitCode != 0)
            {
                SetStatus(_t["ui.restartToFirmwareFailed", process.ExitCode], isAlert: true);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            SetStatus(_t["ui.openFailed", exception.Message], isAlert: true);
        }
    }

    private void OnLanguageChosen(string code)
    {
        if (code == _t.Language)
        {
            return;
        }

        _t = Translator.Load(code);
        SaveSettings(_settings with { Language = code });
        ApplyLanguage();
    }

    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        if (settings.TrySave(AppSettings.DefaultSettingsPath) is { } failure)
        {
            SetStatus(_t["ui.settingsSaveFailed", failure], isAlert: true);
        }
    }

    private void ApplyLanguage()
    {
        Font = Ui.FontFor(_t.Language);
        BuildMenu();
        ApplyEmphasis();
        Text = $"{_t["ui.appName"]} v{CheckReport.CurrentToolVersion}";
        _copyrightLink.Text = _t["ui.copyright", DateTime.Today.Year];
        _viewFullButton.Text = _t["ui.viewFull"];
        _viewModelsButton.Text = _t["ui.viewModels"];
        _elevateButton.Text = _t["ui.restartAdmin"];
        if (_hardwareGroup is not null && _monitorGroup is not null && _controlGroup is not null && _resultGroup is not null)
        {
            _hardwareGroup.Text = _t["ui.hardware"];
            _monitorGroup.Text = _t["ui.monitor"];
            _controlGroup.Text = _t["ui.control"];
            _resultGroup.Text = _t["ui.result"];
        }

        _cpuTemperatureTile.SetCaption(_t["ui.cpuTemperature"]);
        _cpuLoadTile.SetCaption(_t["ui.cpuLoad"]);
        _gpuTemperatureTile.SetCaption(_t["ui.gpuTemperature"]);
        _gpuLoadTile.SetCaption(_t["ui.gpuLoad"]);
        _memoryLoadTile.SetCaption(_t["ui.memoryLoad"]);
        _diskLoadTile.SetCaption(_t["ui.diskLoad"]);
        _uploadTile.SetCaption(_t["ui.upload"]);
        _downloadTile.SetCaption(_t["ui.download"]);
        _targetLabel.Text = _t["ui.targetGpu"];
        _gpuOnlyButton.Text = _t["ui.gpuOnly"];
        _cpuOnlyButton.Text = _t["ui.cpuOnly"];
        _detailsButton.Text = _t["ui.details"];
        _diskHealthButton.Text = _t["ui.diskHealth"];
        _cpuInfoButton.Text = _t["ui.cpuInfo"];
        _memoryTestButton.Text = _t["ui.memoryTest"];
        _diskTestButton.Text = _t["ui.diskTest"];
        _speakerTestButton.Text = _t["ui.speakerTest"];
        _micTestButton.Text = _t["ui.micTest"];
        StyleViewSwitch();

        if (!IsTestRunning)
        {
            _phaseLabel.Text = _t["ui.notRunning"];
        }

        FillTargetBox();
        RenderHardware();
        RenderResults();
        UpdateCommandStates();
        FitMessages();
    }

    /// <summary>Explicit fonts do not inherit from the form, so emphasis is rebuilt whenever the language font changes.</summary>
    private void ApplyEmphasis()
    {
        Control[] emphasized = [_overallLabel, _startButton];
        foreach (var control in emphasized)
        {
            control.Font = new Font(Font, FontStyle.Bold);
        }
    }

    /// <summary>Messages wrap at the current window width rather than running out of the window.</summary>
    private void FitMessages()
    {
        var spacing = LogicalToDeviceUnits(Ui.SPACING * 6);
        _noticeLabel.MaximumSize = new Size(Math.Max(spacing, ClientSize.Width - _elevateButton.Width - (spacing * 2)), 0);
        _statusLabel.MaximumSize = new Size(Math.Max(spacing, ClientSize.Width - _copyrightLink.Width - (spacing * 2)), 0);
    }

    private async Task LoadHardwareAsync()
    {
        _phaseLabel.Text = _t["ui.preparingSensors"];
        await PrepareSensorDriverAsync();
        _phaseLabel.Text = _t["ui.loading"];
        _hardware = await Task.Run(HardwareInfoService.Read);
        if (IsDisposed)
        {
            return;
        }

        FillTargetBox();
        RenderHardware();
        _phaseLabel.Text = _t["ui.notRunning"];
        UpdateCommandStates();
        StartLiveMonitor();
        StartActivityMonitor();
        _clockTimer.Start();
    }

    /// <summary>Installs the built-in sensor driver when it is missing. Everything else still works if this fails.</summary>
    private async Task PrepareSensorDriverAsync()
    {
        var outcome = await Task.Run(() => PawnIoSetup.Install(HardwareInfoService.IsAdministrator(), HardwareInfoService.IsPawnIoInstalled()));
        _isDriverOurs = outcome.Result is DriverSetupResult.Installed or DriverSetupResult.RebootRequired;
        _isDriverPackageOurs = _isDriverOurs && !outcome.HadDriverBefore;
        switch (outcome.Result)
        {
            case DriverSetupResult.Failed:
                SetStatus(_t["ui.driverFailed", outcome.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-"], isAlert: true);
                break;
            case DriverSetupResult.RebootRequired:
                SetStatus(_t["ui.driverReboot"]);
                break;
        }
    }

    private void StartActivityMonitor()
    {
        _activityMonitor = new ActivityMonitor();
        _activityMonitor.Sampled += snapshot => PostToUi(() => ShowActivity(snapshot));
        _activityMonitor.Start();
    }

    private void ShowActivity(ActivitySnapshot snapshot)
    {
        ShowUsage(_memoryLoadTile, Format(snapshot.MemoryLoadPercent, "0", "%"), snapshot.MemoryLoadPercent / PERCENT);
        ShowUsage(_diskLoadTile, Format(snapshot.DiskBusyPercent, "0", "%"), snapshot.DiskBusyPercent / PERCENT);
        ShowUsage(_uploadTile, Ui.FormatRate(snapshot.UploadBytesPerSecond), MetricLevels.LinkFraction(snapshot.UploadBytesPerSecond, _hardware?.Network));
        ShowUsage(_downloadTile, Ui.FormatRate(snapshot.DownloadBytesPerSecond), MetricLevels.LinkFraction(snapshot.DownloadBytesPerSecond, _hardware?.Network));
    }

    private static void ShowUsage(MetricTile tile, string text, double? fraction) =>
        tile.Show(text, fraction, MetricLevels.ForUsage(fraction));

    private void ShowDiskHealth()
    {
        if (_hardware?.DiskHealth is not { } disks)
        {
            return;
        }

        using var form = new DiskHealthForm(_t, disks, Font);
        form.ShowDialog(this);
    }

    private void ShowCpuInfo()
    {
        if (_hardware is { } hardware)
        {
            using var form = new CpuInfoForm(_t, hardware, Font);
            form.ShowDialog(this);
        }
    }

    private void FillTargetBox()
    {
        var selected = _targetBox.SelectedIndex;
        _targetBox.Items.Clear();
        var candidates = TestTargets();

        // One card leaves nothing to choose, so the row appears only with two or more.
        var hasChoice = candidates.Count > 1;
        _targetPanel.Visible = hasChoice;
        _rightPanel.RowStyles[1].Height = LogicalToDeviceUnits(hasChoice ? CONTROL_SECTION_HEIGHT : CONTROL_SECTION_COMPACT_HEIGHT);
        for (var index = 0; index < candidates.Count; index++)
        {
            // Numbered because two identical cards would otherwise read the same.
            _targetBox.Items.Add($"{index + 1}: {DisplayNames.GpuModel(candidates[index].Model ?? "")} {_t[EnumText.Key("gpu.kind", candidates[index].Kind)]}");
        }

        if (selected >= 0 && selected < candidates.Count)
        {
            _targetBox.SelectedIndex = selected;
        }
        else if (_hardware is { } hardware && GpuInfoCollector.DefaultStressTarget(hardware.Gpus) is { } chosen)
        {
            _targetBox.SelectedIndex = candidates.ToList().IndexOf(chosen);
        }
    }

    private List<GpuInfo> TestTargets() => _hardware is { } hardware ? GpuInfoCollector.StressTargets(hardware.Gpus) : [];

    private void OnViewChanged()
    {
        var isModelsOnly = !_viewFullButton.Checked;
        if (_isApplyingView || _settings.IsModelsOnlyView == isModelsOnly)
        {
            return;
        }

        SaveSettings(_settings with { IsModelsOnlyView = isModelsOnly });
        RenderHardware();
    }

    private void StyleViewSwitch()
    {
        _isApplyingView = true;
        _viewFullButton.Checked = !_settings.IsModelsOnlyView;
        _viewModelsButton.Checked = _settings.IsModelsOnlyView;
        _isApplyingView = false;
    }
    private void RenderHardware()
    {
        if (_hardware is null)
        {
            _hardwareList.SetRows([new InfoRow(_t["ui.loading"], "", IsHeader: true)]);
            return;
        }

        var driveHealth = new Queue<DiskHealth>((_hardware.DiskHealth ?? []).Where(health => health.Status != DiskHealthStatus.Unknown));
        var rows = new List<InfoRow>();
        foreach (var line in HardwareSummary.Lines(_t, _hardware, !_settings.IsModelsOnlyView))
        {
            // Health lines come in drive order, so each one takes its color from the next drive.
            var needsAttention = line.Label == _t["report.diskHealth"] && driveHealth.TryDequeue(out var health) && DiskHealthSummary.NeedsAttention(health);
            rows.Add(new InfoRow(line.Label, line.Value, needsAttention ? Ui.AlertColor : null));
        }

        var cooling = CoolingEstimator.Estimate(_sensors, _hardware.Environment.IsSensorAccessLimited);
        if (!_settings.IsModelsOnlyView)
        {
            if (cooling.Outcome != CoolingOutcome.NoData)
            {
                rows.Add(new InfoRow(_t["ui.cooling"], EnumText.CoolingEstimate(_t, cooling.Outcome), cooling.Outcome == CoolingOutcome.AbnormalWiring ? Ui.AlertColor : null));
            }

            if (cooling.CoolerName is { } coolerName)
            {
                rows.Add(new InfoRow(_t["summary.cooler"], coolerName));
            }

            rows.AddRange(HardwareSummary.PeripheralLines(_t, _hardware.Peripherals).Select(line => new InfoRow(line.Label, line.Value)));
            if (HardwareSummary.LastBootLine(_t, _hardware) is { } lastBoot)
            {
                rows.Add(new InfoRow(lastBoot.Label, lastBoot.Value));
            }
        }

        _hardwareList.SetRows(rows);
        RenderNotice(cooling);
    }

    private void RenderNotice(CoolingEstimate cooling)
    {
        if (_hardware is null)
        {
            return;
        }

        var environment = _hardware.Environment;
        var lines = new List<string>();
        if (!environment.IsAdministrator)
        {
            lines.Add(_t["environment.needAdministrator"]);
        }
        else if (!environment.IsPawnIoInstalled)
        {
            lines.Add(_t["environment.needPawnIo"]);
        }

        if (cooling.Outcome == CoolingOutcome.AbnormalWiring)
        {
            lines.Add(_t["cooling.note.checkCoolerWiring"]);
        }

        if (TestTargets().Count == 0)
        {
            lines.Add(_t["ui.noDiscreteGpu"]);
        }

        if (_hardware.Battery is not null && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline)
        {
            lines.Add(_t["ui.onBattery"]);
        }

        lines.AddRange(_hardware.Diagnostics.Select(entry => $"{_t["section.diagnostics"]} - {entry.Source}: {entry.Message}"));
        _noticeLabel.Text = string.Join(Environment.NewLine, lines);
        _noticePanel.Visible = lines.Count > 0;
        _elevateButton.Visible = !environment.IsAdministrator;
    }

    private void StartLiveMonitor()
    {
        if (IsDisposed || IsTestRunning || _liveSampler is not null)
        {
            return;
        }

        _sensorSource ??= new LhmSensorSource();
        var session = new MonitoringSession();
        var sampler = new SensorSampler(_sensorSource, session, LiveInterval);
        sampler.Sampled += readings => PostToUi(() =>
        {
            var isFirstReading = _sensors.Count == 0;
            _sensors = readings;
            UpdateLiveValues(readings);
            if (isFirstReading)
            {
                RenderHardware();
            }
        });
        _liveSampler = sampler;
        sampler.Start();
    }

    private void StopLiveMonitor()
    {
        _liveSampler?.Dispose();
        _liveSampler = null;
    }

    private void UpdateLiveValues(IReadOnlyList<SensorReading> readings)
    {
        var model = SelectedTarget()?.Model;
        ShowMetrics(SensorSelector.CpuTemperature(readings), SensorSelector.CpuLoad(readings), SensorSelector.GpuTemperature(readings, model), SensorSelector.GpuLoad(readings, model));
    }

    private void ShowMetrics(double? cpuTemperature, double? cpuLoad, double? gpuTemperature, double? gpuLoad)
    {
        var profile = _settings.ToProfile();
        _cpuTemperatureTile.Show(Format(cpuTemperature, "0.#", "°C"), cpuTemperature / PERCENT, MetricLevels.ForTemperature(cpuTemperature, profile.CpuProtectionCelsius));
        ShowUsage(_cpuLoadTile, Format(cpuLoad, "0", "%"), cpuLoad / PERCENT);
        _gpuTemperatureTile.Show(Format(gpuTemperature, "0.#", "°C"), gpuTemperature / PERCENT, MetricLevels.ForTemperature(gpuTemperature, profile.GpuProtectionCelsius));
        ShowUsage(_gpuLoadTile, Format(gpuLoad, "0", "%"), gpuLoad / PERCENT);
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window closed between the check and the post, so there is nothing left to update.
        }
    }

    private GpuInfo? SelectedTarget()
    {
        var targets = TestTargets();
        return _targetBox.SelectedIndex >= 0 && _targetBox.SelectedIndex < targets.Count ? targets[_targetBox.SelectedIndex] : null;
    }

    private async Task RunTestAsync(bool runIdle, bool runGpu, bool runCpu)
    {
        if (IsTestRunning || _hardware is null)
        {
            return;
        }

        StopLiveMonitor();
        _sensorSource ??= new LhmSensorSource();
        _testStop = new CancellationTokenSource();
        _lastRun = null;
        RenderResults();
        UpdateCommandStates();
        SetStatus(null);

        var plan = new TestPlan(_settings.ToProfile(), runIdle, runGpu, runCpu, SelectedTarget());
        var controller = new TestController(_sensorSource, gpu => new GpuStressLoad(gpu.AdapterLuid!), new CpuStressLoad())
        {
            ConfirmUnmonitored = ConfirmUnmonitoredAsync,
        };
        controller.ProgressChanged += progress => PostToUi(() => ShowProgress(progress));
        var stop = _testStop.Token;

        try
        {
            _lastRun = await Task.Run(() => controller.RunAsync(plan, stop), CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Loads and sensors report failures as results, so reaching here is itself worth showing.
            SetStatus(_t["ui.testFailed", exception.Message], isAlert: true);
        }
        finally
        {
            _testStop.Dispose();
            _testStop = null;
        }

        _phaseLabel.Text = _t["ui.notRunning"];
        _progressBar.Value = 0;
        RenderResults();
        UpdateCommandStates();
        if (_closeAfterTest)
        {
            Close();
            return;
        }

        StartLiveMonitor();
    }

    private async Task RunSpeakerTestAsync()
    {
        await RunToolAsync(_t["ui.speakerPlaying"], async (_, _) =>
        {
            await Task.Run(AudioCheck.PlayTone);
            var heard = MessageBox.Show(this, _t["ui.speakerConfirm"], _t["ui.speakerTest"], MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            _audioCheck = _audioCheck with { SpeakerHeard = heard };
        });
    }

    private async Task RunMicrophoneTestAsync()
    {
        if (_hardware?.Peripherals?.Any(item => item.Kind == PeripheralKind.Microphone) != true)
        {
            SetStatus(_t["ui.noMicrophone"], isAlert: true);
            return;
        }

        await RunToolAsync(_t["ui.micRecording"], async (_, _) =>
        {
            await Task.Run(() => AudioCheck.RecordAndPlayBack(() => PostToUi(() => _phaseLabel.Text = _t["ui.micPlaying"])));
            var heard = MessageBox.Show(this, _t["ui.micConfirm"], _t["ui.micTest"], MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            _audioCheck = _audioCheck with { MicrophoneHeard = heard };
        });
    }

    private async Task RunMemoryTestAsync()
    {
        var test = new MemoryTest(MemoryTest.DefaultDuration);
        await RunToolAsync(_t["ui.memoryTest"], async (progress, stop) => _memoryResult = await test.RunAsync(progress, stop));
    }

    private async Task RunDiskTestAsync()
    {
        using var form = new DiskTestForm(_t, Font);
        if (form.ShowDialog(this) != DialogResult.OK || form.SelectedRoot is not { } root)
        {
            return;
        }

        var test = new DiskTest(DiskTest.FolderForDrive(root, Path.GetTempPath()));
        await RunToolAsync(_t["ui.diskTest"], async (progress, stop) => _diskResult = await test.RunAsync(progress, stop));
    }

    /// <summary>Runs a memory or drive test with the same stop button and progress bar as the stress tests.</summary>
    private async Task RunToolAsync(string name, Func<IProgress<double>, CancellationToken, Task> run)
    {
        if (IsTestRunning || _hardware is null)
        {
            return;
        }

        _testStop = new CancellationTokenSource();
        UpdateCommandStates();
        SetStatus(null);
        _phaseLabel.Text = name;
        _progressBar.Value = 0;
        var progress = new Progress<double>(fraction => _progressBar.Value = (int)(Math.Clamp(fraction, 0, 1) * PROGRESS_MAX));
        try
        {
            await run(progress, _testStop.Token);
        }
        catch (Exception exception)
        {
            SetStatus(_t["ui.testFailed", exception.Message], isAlert: true);
        }
        finally
        {
            _testStop.Dispose();
            _testStop = null;
        }

        _phaseLabel.Text = _t["ui.notRunning"];
        _progressBar.Value = 0;
        RenderResults();
        UpdateCommandStates();
        if (_closeAfterTest)
        {
            Close();
        }
    }

    private Task<bool> ConfirmUnmonitoredAsync(TestPhase phase, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>();
        PostToUi(() =>
        {
            var answer = !cancellationToken.IsCancellationRequested && MessageBox.Show(
                this,
                _t["ui.confirmUnmonitored", EnumText.Phase(_t, phase)],
                _t["ui.confirmTitle"],
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            completion.TrySetResult(answer);
        });
        return completion.Task;
    }

    private void ShowProgress(TestProgress progress)
    {
        if (!IsTestRunning)
        {
            return;
        }

        var elapsed = Math.Min(progress.PhaseElapsedSeconds, progress.PhasePlannedSeconds);
        _phaseLabel.Text = _t["ui.phaseProgress", EnumText.Phase(_t, progress.Phase), Math.Round(elapsed), Math.Round(progress.PhasePlannedSeconds)];
        _progressBar.Value = progress.PhasePlannedSeconds > 0 ? (int)(elapsed / progress.PhasePlannedSeconds * PROGRESS_MAX) : 0;
        ShowMetrics(progress.CpuTemperature, progress.CpuLoad, progress.GpuTemperature, progress.GpuLoad);
    }

    private void RequestStop() => _testStop?.Cancel();

    private void UpdateCommandStates()
    {
        var isReady = _hardware is not null;
        var isRunning = IsTestRunning;
        var hasTarget = TestTargets().Count > 0;
        _startButton.Enabled = isReady && (isRunning || hasTarget);
        _startButton.Text = _t[isRunning ? "ui.stop" : "ui.startAll"];
        _gpuOnlyButton.Enabled = isReady && !isRunning && hasTarget;
        _cpuOnlyButton.Enabled = isReady && !isRunning;
        _targetBox.Enabled = !isRunning;
        _detailsButton.Enabled = _lastRun is not null;
        _diskHealthButton.Enabled = isReady;
        _cpuInfoButton.Enabled = isReady;
        _memoryTestButton.Enabled = isReady && !isRunning;
        _diskTestButton.Enabled = isReady && !isRunning;
        _speakerTestButton.Enabled = isReady && !isRunning;
        _micTestButton.Enabled = isReady && !isRunning;
        _elevateButton.Enabled = !isRunning;
        if (_menu is { } menu)
        {
            menu.SetEnabled(MenuCommand.Report, isReady && !isRunning);
            menu.SetEnabled(MenuCommand.Copy, isReady);
            menu.SetEnabled(MenuCommand.OpenReport, _lastReport is not null);
            menu.SetEnabled(MenuCommand.Settings, !isRunning);

            // Only a PC that booted through UEFI with Secure Boot off has anything to change there.
            menu.SetEnabled(MenuCommand.RestartToFirmware, !isRunning && _hardware?.Security?.SecureBootEnabled == false);
            for (var index = 0; index < LanguageChoices.Length; index++)
            {
                menu.SetEnabled(MenuCommand.FirstLanguage + index, !isRunning);
            }
        }
    }

    private void RenderResults()
    {
        var rows = new List<InfoRow>();
        if (_lastRun is not { } run)
        {
            var hasToolResult = _memoryResult is not null || _diskResult is not null || _audioCheck != new AudioCheckResult(null, null);
            var hasProblem = ToolTestSummary.HasProblem(_memoryResult) || ToolTestSummary.HasProblem(_diskResult);
            _overallLabel.Text = hasProblem ? _t["overall.needsReview"] : hasToolResult ? "" : IsTestRunning ? _t["ui.running"] : _t["ui.noResult"];
            _overallLabel.ForeColor = hasProblem ? Ui.AlertColor : SystemColors.ControlText;
            AddToolRows(rows);
            _resultList.SetRows(rows);
            return;
        }

        _overallLabel.Text = EnumText.Overall(_t, run.Overall);
        _overallLabel.ForeColor = run.Overall switch
        {
            OverallResult.NoObviousIssue => Ui.SuccessColor,
            OverallResult.NeedsReview => Ui.AlertColor,
            _ => Ui.WarningColor,
        };

        foreach (var phase in run.Phases.Where(phase => phase.IsRequested))
        {
            Color? color = phase.Status switch
            {
                TestStatus.IssueFound => Ui.AlertColor,
                TestStatus.Passed => null,
                _ => Ui.WarningColor,
            };
            rows.Add(new InfoRow(EnumText.Phase(_t, phase.Phase), EnumText.Status(_t, phase.Status), color));
        }

        foreach (var row in TemperatureSummary.Build(run, SelectedTarget()).Where(row => row.State is not (TemperatureRowState.NotRun or TemperatureRowState.Unavailable)))
        {
            // The idle label needs the device name; the load labels already contain it.
            var condition = _t[EnumText.Key("report.condition", row.Phase)];
            var label = row.Phase == TestPhase.Idle ? $"{(row.IsCpu ? "CPU" : "GPU")} {condition}" : condition;
            rows.Add(new InfoRow(label, TemperatureText(row)));
        }

        rows.AddRange(LoadMetricSummary.Build(run).Select(row => new InfoRow(_t[EnumText.Key("metric", row.Metric)], LoadMetricSummary.Text(_t, row))));
        rows.AddRange(run.Anomalies.Select(anomaly => new InfoRow(EnumText.Phase(_t, anomaly.Phase), EnumText.Anomaly(_t, anomaly.Code), Ui.AlertColor)));
        AddToolRows(rows);
        _resultList.SetRows(rows);
    }

    private void AddHeardRow(List<InfoRow> rows, string name, bool? heard)
    {
        if (heard is { } value)
        {
            rows.Add(new InfoRow(name, _t[value ? "audioCheck.heard" : "audioCheck.notHeard"], value ? null : Ui.AlertColor));
        }
    }

    private void AddToolRows(List<InfoRow> rows)
    {
        if (_memoryResult is { } memory)
        {
            rows.Add(new InfoRow(_t["ui.memoryTest"], ToolTestSummary.Text(_t, memory), ToolTestSummary.HasProblem(memory) ? Ui.AlertColor : null));
        }

        if (_diskResult is { } disk)
        {
            rows.Add(new InfoRow(_t["ui.diskTest"], ToolTestSummary.Text(_t, disk), ToolTestSummary.HasProblem(disk) ? Ui.AlertColor : null));
        }

        AddHeardRow(rows, _t["ui.speakerTest"], _audioCheck.SpeakerHeard);
        AddHeardRow(rows, _t["ui.micTest"], _audioCheck.MicrophoneHeard);
    }

    private string TemperatureText(TemperatureRow row) => row.State switch
    {
        TemperatureRowState.Measured => _t["test.statistics", Number(row.Minimum), Number(row.Maximum), Number(row.Average), row.ValidSamples],
        _ => _t["status.insufficientData"],
    };

    private void ShowDetails()
    {
        if (_lastRun is not { } run)
        {
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(EnumText.Overall(_t, run.Overall));
        text.AppendLine(CultureInfo.InvariantCulture, $"{run.StartedAt:yyyy-MM-dd HH:mm:ss} - {run.EndedAt:HH:mm:ss}");
        text.AppendLine(_t["ui.sensorReads", run.SensorReads, run.SensorReadFailures]);
        text.AppendLine();
        foreach (var phase in run.Phases)
        {
            var load = phase.AverageLoadPercent is { } value ? $"{value.ToString("0", CultureInfo.InvariantCulture)} %" : _t["common.unavailable"];
            text.AppendLine(CultureInfo.InvariantCulture, $"{EnumText.Phase(_t, phase.Phase)}: {EnumText.Status(_t, phase.Status)} | {phase.ActualSeconds:0}/{phase.PlannedSeconds:0} s | {_t["ui.cpuLoadShort"]} {load}");
        }

        text.AppendLine();
        foreach (var item in run.Statistics.Where(item => item.SensorType == "Temperature").OrderBy(item => item.Phase).ThenBy(item => item.HardwareName).ThenBy(item => item.SensorName))
        {
            var values = item.Average is null
                ? _t["common.unavailable"]
                : _t["test.statistics", Number(item.Minimum), Number(item.Maximum), Number(item.Average), item.ValidSamples];
            text.AppendLine(CultureInfo.InvariantCulture, $"{EnumText.Phase(_t, item.Phase)} | {item.HardwareName} / {item.SensorName}: {values}");
        }

        foreach (var anomaly in run.Anomalies)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"! {EnumText.Phase(_t, anomaly.Phase)}: {EnumText.Anomaly(_t, anomaly.Code)}{(anomaly.Detail is null ? "" : $" ({anomaly.Detail})")}");
        }

        using var form = new Form
        {
            Text = _t["ui.details"],
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(LogicalToDeviceUnits(820), LogicalToDeviceUnits(560)),
            Font = Font,
            Icon = Icon,
            MinimizeBox = false,
        };
        form.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = text.ToString() });
        form.ShowDialog(this);
    }

    private void GenerateReport()
    {
        if (_hardware is null || IsTestRunning)
        {
            return;
        }

        var report = new CheckReport(
            CheckReport.CURRENT_SCHEMA_VERSION,
            CheckReport.CurrentToolVersion,
            DateTimeOffset.Now,
            _hardware,
            _sensors,
            CoolingEstimator.Estimate(_sensors, _hardware.Environment.IsSensorAccessLimited),
            _lastRun,
            _memoryResult,
            _diskResult,
            _audioCheck);

        try
        {
            _lastReport = ReportStore.Save(report, _settings.EffectiveReportFolder, _t);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus(_t["ui.saveFailed", exception.Message], isAlert: true);
            return;
        }

        SetStatus(_t["ui.saved", _lastReport.HtmlPath]);
        UpdateCommandStates();
        OpenInShell(_lastReport.HtmlPath);
    }

    private void CopyConfiguration()
    {
        if (_hardware is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(HardwareSummary.AsText(HardwareSummary.Lines(_t, _hardware, !_settings.IsModelsOnlyView).Append(_settings.IsModelsOnlyView ? null : HardwareSummary.LastBootLine(_t, _hardware)).OfType<SummaryLine>()));
            SetStatus(_t["ui.copied"]);
        }
        catch (Exception exception) when (exception is ExternalException or ThreadStateException)
        {
            SetStatus(_t["ui.copyFailed", exception.Message], isAlert: true);
        }
    }

    private void OpenReportFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.EffectiveReportFolder);
            OpenInShell(_settings.EffectiveReportFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus(_t["ui.saveFailed", exception.Message], isAlert: true);
        }
    }

    private void OpenInShell(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            SetStatus(_t["ui.openFailed", exception.Message], isAlert: true);
        }
    }

    private void OpenSettingsForm()
    {
        using var form = new SettingsForm(_t, _settings, Font);
        if (form.ShowDialog(this) == DialogResult.OK && form.Result is { } settings)
        {
            SaveSettings(settings);
        }
    }

    private void RestartElevated()
    {
        if (Environment.ProcessPath is not { } path)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            Close();
        }
        catch (Win32Exception exception)
        {
            // Declining the Windows prompt lands here, and the current window simply stays open.
            SetStatus(_t["ui.elevationFailed", exception.Message], isAlert: true);
        }
    }

    private void SetStatus(string? text, bool isAlert = false)
    {
        _statusText = text;
        _statusLabel.Text = _statusText ?? "";
        _statusLabel.ForeColor = isAlert ? Ui.AlertColor : SystemColors.ControlText;
    }
}
