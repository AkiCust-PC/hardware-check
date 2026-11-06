using AkikazuHardwareCheck.Core.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// The same scan/confirm/clean flow as the diagnostics window, reusable from the main window and the floating ball.
/// Only the four known disposable locations are offered; nothing is deleted before the confirmation.
/// </summary>
internal sealed class JunkCleanupForm : Form
{
    private readonly Translator _t;
    private readonly CheckedListBox _items = new() { CheckOnClick = true, IntegralHeight = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly Label _status = Ui.Label();
    private readonly Button _scan = Ui.Button();
    private readonly Button _clean = Ui.Button();
    private readonly List<JunkEntry> _entries = [];

    public JunkCleanupForm(Translator t, Font font)
    {
        _t = t;
        Font = font;
        Text = t["maintenance.cleanup"];
        Icon = Ui.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(560), LogicalToDeviceUnits(400));
        MinimumSize = new Size(LogicalToDeviceUnits(480), LogicalToDeviceUnits(320));

        var hint = new Label
        {
            Text = t["maintenance.cleanupHint"],
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(500), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, Ui.SPACING),
        };
        _scan.Text = t["maintenance.scan"];
        _scan.Click += async (_, _) => await ScanAsync();
        _clean.Text = t["maintenance.clean"];
        _clean.Enabled = false;
        _clean.Click += async (_, _) => await CleanAsync();
        var close = Ui.Button();
        close.Text = t["ui.cancel"];
        close.DialogResult = DialogResult.Cancel;
        var buttons = Ui.Flow();
        buttons.Controls.AddRange([_scan, _clean, close]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(Ui.SPACING * 2) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_items, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        Controls.Add(layout);
        CancelButton = close;
        Ui.ScaleButtons(this);
        Shown += async (_, _) => await ScanAsync();
    }

    private async Task ScanAsync()
    {
        _scan.Enabled = false;
        _clean.Enabled = false;
        _status.Text = _t["maintenance.scanning"];
        JunkScan scan;
        try
        {
            scan = await Task.Run(JunkCleaner.Scan);
        }
        catch (Exception exception)
        {
            _status.Text = _t["maintenance.scanFailed", exception.Message];
            _scan.Enabled = true;
            return;
        }

        if (IsDisposed)
        {
            return;
        }

        _entries.Clear();
        _items.Items.Clear();
        foreach (var entry in scan.Entries)
        {
            _entries.Add(entry);
            _items.Items.Add($"{_t["junk." + entry.Category]} - {MaintenanceCollectors.FormatBytes(entry.Bytes)} ({entry.Files})", false);
        }

        _scan.Enabled = true;
        _clean.Enabled = _entries.Count > 0;
        _status.Text = scan.IsAdministrator ? _t["maintenance.cleanupScanned"] : _t["maintenance.cleanupAdminHint"];
    }

    private async Task CleanAsync()
    {
        var selected = _items.CheckedIndices.Cast<int>().ToList();
        if (selected.Count == 0)
        {
            _status.Text = _t["maintenance.nothingSelected"];
            return;
        }

        if (MessageBox.Show(this, _t["maintenance.cleanConfirm"], _t["maintenance.clean"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var deleted = 0;
        var bytes = 0L;
        var skipped = 0;
        foreach (var index in selected)
        {
            var result = await Task.Run(() => JunkCleaner.Clean(_entries[index].Category));
            deleted += result.DeletedFiles;
            bytes += result.DeletedBytes;
            skipped += result.SkippedFiles;
        }

        _status.Text = _t["maintenance.cleaned", deleted, MaintenanceCollectors.FormatBytes(bytes), skipped];
        await ScanAsync();
    }
}
