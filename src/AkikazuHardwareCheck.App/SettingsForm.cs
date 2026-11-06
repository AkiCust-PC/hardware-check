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
    private const string LICENSE_PREFIX = "license.";

    private readonly AppSettings _original;
    private readonly TextBox _folderBox = new();
    private readonly NumericUpDown _gpuSeconds = Number(MIN_STRESS_SECONDS, MAX_STRESS_SECONDS);
    private readonly NumericUpDown _cpuSeconds = Number(MIN_STRESS_SECONDS, MAX_STRESS_SECONDS);
    private readonly NumericUpDown _cpuLimit = Number(MIN_LIMIT_CELSIUS, MAX_LIMIT_CELSIUS);
    private readonly NumericUpDown _gpuLimit = Number(MIN_LIMIT_CELSIUS, MAX_LIMIT_CELSIUS);

    public SettingsForm(Translator t, AppSettings settings, Font font)
    {
        _original = settings;
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

        var browseButton = Ui.Button();
        browseButton.Text = t["ui.browse"];
        browseButton.Click += (_, _) => BrowseFolder();
        var folderRow = Ui.Flow();
        folderRow.WrapContents = false;
        folderRow.Controls.AddRange([_folderBox, browseButton]);

        var fields = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(LABEL_WIDTH)));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddField(fields, t["ui.reportFolder"], folderRow);
        AddField(fields, t["ui.gpuSeconds"], _gpuSeconds);
        AddField(fields, t["ui.cpuSeconds"], _cpuSeconds);
        AddField(fields, t["ui.cpuLimit"], _cpuLimit);
        AddField(fields, t["ui.gpuLimit"], _gpuLimit);

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
        root.Controls.Add(fields, 0, 0);
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
    };

    private static NumericUpDown Number(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
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
