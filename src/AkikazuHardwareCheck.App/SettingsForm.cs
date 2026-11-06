using System.Diagnostics;
using AkikazuHardwareCheck.Core.Configuration;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.App;

internal sealed class SettingsForm : Form
{
    private const int MIN_STRESS_SECONDS = 60;
    private const int MAX_STRESS_SECONDS = 300;
    private const int MIN_LIMIT_CELSIUS = 60;
    private const int MAX_LIMIT_CELSIUS = 110;
    private const int LABEL_WIDTH = 220;
    private const int FOLDER_BOX_WIDTH = 330;
    private const int TAB_WIDTH = 620;
    private const int TAB_HEIGHT = 390;
    private const string LICENSE_PREFIX = "license.";

    private readonly AppSettings _original;
    private readonly TextBox _folderBox = new();
    private readonly NumericUpDown _gpuSeconds = Number(MIN_STRESS_SECONDS, MAX_STRESS_SECONDS);
    private readonly NumericUpDown _cpuSeconds = Number(MIN_STRESS_SECONDS, MAX_STRESS_SECONDS);
    private readonly NumericUpDown _cpuLimit = Number(MIN_LIMIT_CELSIUS, MAX_LIMIT_CELSIUS);
    private readonly NumericUpDown _gpuLimit = Number(MIN_LIMIT_CELSIUS, MAX_LIMIT_CELSIUS);
    private readonly CheckBox _showTrayIcon = Option();
    private readonly CheckBox _closeToTray = Option();
    private readonly CheckBox _startWithWindows = Option();
    private readonly CheckBox _alwaysOnTop = Option();
    private readonly CheckBox _crosshairEnabled = Option();
    private readonly NumericUpDown _hudFont = new() { Minimum = 8, Maximum = 32, Margin = new Padding(0, 0, 0, Ui.SPACING) };
    private readonly CheckedListBox _hudItems = new() { CheckOnClick = true, IntegralHeight = false, Height = 100, Margin = new Padding(0) };
    private Color _hudColor;
    private readonly Translator _t;
    private CrosshairSettings _crosshair;

    public SettingsForm(Translator t, AppSettings settings, Font font)
    {
        _t = t;
        _original = settings;
        _crosshair = CrosshairSettings.FromSettings(settings);
        Font = font;
        Text = t["ui.settings"];
        Icon = Ui.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var profile = settings.ToProfile();
        _folderBox.Text = settings.EffectiveReportFolder;
        _folderBox.Width = LogicalToDeviceUnits(FOLDER_BOX_WIDTH);
        _gpuSeconds.Value = (decimal)profile.GpuStressSeconds;
        _cpuSeconds.Value = (decimal)profile.CpuStressSeconds;
        _cpuLimit.Value = (decimal)Math.Clamp(profile.CpuProtectionCelsius, MIN_LIMIT_CELSIUS, MAX_LIMIT_CELSIUS);
        _gpuLimit.Value = (decimal)Math.Clamp(profile.GpuProtectionCelsius, MIN_LIMIT_CELSIUS, MAX_LIMIT_CELSIUS);
        _showTrayIcon.Checked = settings.ShowTrayIcon;
        _closeToTray.Checked = settings.CloseToTray;
        _startWithWindows.Checked = settings.StartWithWindows || StartupRegistration.IsEnabled();
        _alwaysOnTop.Checked = settings.IsAlwaysOnTop;
        _crosshairEnabled.Checked = settings.CrosshairEnabled;
        _hudFont.Value = Math.Clamp(settings.HudFontSize, 8, 32);
        _hudColor = Color.FromArgb(settings.HudColorArgb);
        _hudItems.Height = LogicalToDeviceUnits(100);
        var hudLabels = new[] { t["hud.cpuLoad"], t["hud.cpuTemp"], t["hud.gpuLoad"], t["hud.gpuTemp"], t["hud.memory"] };
        for (var index = 0; index < hudLabels.Length; index++)
        {
            _hudItems.Items.Add(hudLabels[index], (settings.HudItems & (1 << index)) != 0);
        }

        var tabs = new TabControl
        {
            Width = LogicalToDeviceUnits(TAB_WIDTH),
            Height = LogicalToDeviceUnits(TAB_HEIGHT),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        tabs.TabPages.Add(BuildGeneralPage(t));
        tabs.TabPages.Add(BuildTestPage(t));
        tabs.TabPages.Add(BuildCrosshairPage(t));
        tabs.TabPages.Add(BuildGamePage(t));

        var okButton = Ui.Button();
        okButton.Text = t["ui.ok"];
        okButton.DialogResult = DialogResult.OK;
        var cancelButton = Ui.Button();
        cancelButton.Text = t["ui.cancel"];
        cancelButton.DialogResult = DialogResult.Cancel;
        var licensesButton = Ui.Button();
        licensesButton.Text = t["ui.licenses"];
        licensesButton.Click += (_, _) => OpenLicenses();
        var buttons = Ui.Flow();
        buttons.Controls.AddRange([okButton, cancelButton, licensesButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(Ui.SPACING * 2) };
        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        Ui.ScaleButtons(this);
    }

    public AppSettings? Result => DialogResult != DialogResult.OK ? null : _original with
    {
        ReportFolder = string.IsNullOrWhiteSpace(_folderBox.Text) ? null : _folderBox.Text.Trim(),
        GpuStressSeconds = (double)_gpuSeconds.Value,
        CpuStressSeconds = (double)_cpuSeconds.Value,
        CpuProtectionCelsius = (double)_cpuLimit.Value,
        GpuProtectionCelsius = (double)_gpuLimit.Value,
        ShowTrayIcon = _showTrayIcon.Checked,
        CloseToTray = _closeToTray.Checked,
        StartWithWindows = _startWithWindows.Checked,
        IsAlwaysOnTop = _alwaysOnTop.Checked,
        CrosshairEnabled = _crosshairEnabled.Checked,
        CrosshairShape = (int)_crosshair.Shape,
        CrosshairColorArgb = _crosshair.Color.ToArgb(),
        CrosshairLength = _crosshair.Length,
        CrosshairThickness = _crosshair.Thickness,
        CrosshairGap = _crosshair.Gap,
        CrosshairOpacity = _crosshair.Opacity,
        HudFontSize = (int)_hudFont.Value,
        HudColorArgb = _hudColor.ToArgb(),
        HudItems = Enumerable.Range(0, _hudItems.Items.Count).Aggregate(0, (value, index) => _hudItems.GetItemChecked(index) ? value | (1 << index) : value),
    };

    private TabPage BuildGeneralPage(Translator t)
    {
        var page = new TabPage(t["settings.general"]);
        var browseButton = Ui.Button();
        browseButton.Text = t["ui.browse"];
        browseButton.Click += (_, _) => BrowseFolder();
        var folderRow = Ui.Flow();
        folderRow.WrapContents = false;
        folderRow.Controls.AddRange([_folderBox, browseButton]);

        var fields = Fields();
        AddField(fields, t["ui.reportFolder"], folderRow);
        AddField(fields, t["ui.showTrayIcon"], _showTrayIcon);
        AddField(fields, t["ui.closeToTray"], _closeToTray);
        AddField(fields, t["ui.startWithWindows"], _startWithWindows);
        AddField(fields, t["ui.alwaysOnTop"], _alwaysOnTop);
        page.Controls.Add(fields);
        return page;
    }

    private TabPage BuildTestPage(Translator t)
    {
        var page = new TabPage(t["settings.tests"]);
        var fields = Fields();
        AddField(fields, t["ui.gpuSeconds"], _gpuSeconds);
        AddField(fields, t["ui.cpuSeconds"], _cpuSeconds);
        AddField(fields, t["ui.cpuLimit"], _cpuLimit);
        AddField(fields, t["ui.gpuLimit"], _gpuLimit);
        page.Controls.Add(fields);
        return page;
    }

    private TabPage BuildCrosshairPage(Translator t)
    {
        var page = new TabPage(t["settings.crosshair"]);
        var layout = Fields();
        _crosshairEnabled.Text = t["crosshair.enable"];
        AddField(layout, t["crosshair.enable"], _crosshairEnabled);

        var details = Ui.Button();
        details.Text = t["crosshair.settings"];
        details.Click += (_, _) => OpenCrosshairEditor();
        AddField(layout, t["crosshair.shape"], details);

        var hint = new Label
        {
            Text = t["crosshair.hint"],
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(TAB_WIDTH - 60), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, Ui.SPACING, 0, 0),
        };
        var row = layout.RowCount++;
        layout.Controls.Add(hint, 0, row);
        layout.SetColumnSpan(hint, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildGamePage(Translator t)
    {
        var page = new TabPage(t["settings.game"]);
        var colorButton = Ui.Button();
        colorButton.Text = t["hud.color"];
        colorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _hudColor, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _hudColor = dialog.Color;
            }
        };
        var fields = Fields();
        AddField(fields, t["hud.fontSize"], _hudFont);
        AddField(fields, t["hud.color"], colorButton);
        AddField(fields, t["hud.items"], _hudItems);
        page.Controls.Add(fields);
        return page;
    }

    private void OpenCrosshairEditor()
    {
        using var form = new CrosshairSettingsForm(_t, _crosshair, Font);
        if (form.ShowDialog(this) == DialogResult.OK && form.Result is { } result)
        {
            _crosshair = result;
        }
    }

    private TableLayoutPanel Fields()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(Ui.SPACING),
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(LABEL_WIDTH)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return panel;
    }

    private static NumericUpDown Number(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Margin = new Padding(0, 0, 0, Ui.SPACING),
    };

    private static CheckBox Option() => new()
    {
        AutoSize = true,
        Margin = new Padding(0, 0, 0, Ui.SPACING),
    };

    private static void AddField(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++;
        var name = Ui.Label();
        name.Text = label;
        panel.Controls.Add(name, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    /// <summary>The download is a single program file, so license texts travel inside it and open on request.</summary>
    public static void OpenLicenses()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AkikazuHardwareCheck-licenses");
        Directory.CreateDirectory(folder);
        var core = typeof(AppSettings).Assembly;
        foreach (var name in core.GetManifestResourceNames().Where(name => name.StartsWith(LICENSE_PREFIX, StringComparison.Ordinal)))
        {
            using var resource = core.GetManifestResourceStream(name);
            using var file = File.Create(Path.Combine(folder, name[LICENSE_PREFIX.Length..]));
            resource?.CopyTo(file);
        }

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = _folderBox.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folderBox.Text = dialog.SelectedPath;
        }
    }

}
