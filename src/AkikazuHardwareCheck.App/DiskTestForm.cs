using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Reporting;
using AkikazuHardwareCheck.Core.Testing;

namespace AkikazuHardwareCheck.App;

/// <summary>Staff pick a drive and confirm the temporary write here, so the test never starts by accident.</summary>
internal sealed class DiskTestForm : Form
{
    private const int MESSAGE_WIDTH = 480;

    private readonly ComboBox _driveBox = Ui.Combo();
    private readonly List<string> _roots;

    public DiskTestForm(Translator t, Font font)
    {
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["ui.diskTest"];
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var drives = DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady).ToList();
        _roots = drives.Select(drive => drive.Name).ToList();
        foreach (var drive in drives)
        {
            _driveBox.Items.Add(t["ui.diskTestDrive", drive.Name, DisplayNames.DiskSize((ulong)drive.AvailableFreeSpace), DisplayNames.DiskSize((ulong)drive.TotalSize)]);
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        _driveBox.SelectedIndex = Math.Max(0, _roots.FindIndex(root => string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase)));
        _driveBox.Dock = DockStyle.Top;

        var message = Ui.Label();
        message.MaximumSize = new Size(LogicalToDeviceUnits(MESSAGE_WIDTH), 0);
        message.Text = t["ui.diskTestConfirm", DisplayNames.MemorySize((ulong)DiskTest.DEFAULT_SIZE_BYTES)];

        var okButton = Ui.Button();
        okButton.Text = t["ui.startTest"];
        okButton.DialogResult = DialogResult.OK;
        okButton.Enabled = _roots.Count > 0;
        var cancelButton = Ui.Button();
        cancelButton.Text = t["ui.cancel"];
        cancelButton.DialogResult = DialogResult.Cancel;
        var buttons = Ui.Flow();
        buttons.Controls.AddRange([okButton, cancelButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(Ui.SPACING * 2) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(MESSAGE_WIDTH)));
        root.Controls.Add(_driveBox, 0, 0);
        root.Controls.Add(message, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        Ui.ScaleButtons(this);
    }

    public string? SelectedRoot => _driveBox.SelectedIndex >= 0 ? _roots[_driveBox.SelectedIndex] : null;
}
