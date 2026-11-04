using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.App;

/// <summary>Shows each drive's health values, one list per drive.</summary>
internal sealed class DiskHealthForm : Form
{
    public DiskHealthForm(Translator t, IReadOnlyList<DiskHealth> disks, Font font)
    {
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["ui.diskHealth"];
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(520));

        var rows = new List<InfoRow>();
        foreach (var disk in disks)
        {
            rows.Add(new InfoRow(disk.Model ?? t["common.unavailable"], "", IsHeader: true));
            foreach (var line in DiskHealthSummary.Details(t, disk))
            {
                var isAlert = DiskHealthSummary.NeedsAttention(disk) && line.Label == t["disk.finding"];
                rows.Add(new InfoRow(line.Label, line.Value, isAlert ? Ui.AlertColor : null));
            }
        }

        var table = new InfoView { Dock = DockStyle.Fill, Padding = new Padding(Ui.SPACING) };
        Controls.Add(table);
        table.SetRows(rows);
    }
}
