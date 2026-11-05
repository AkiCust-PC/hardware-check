using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;

namespace AkikazuHardwareCheck.App;

/// <summary>Shows the CPU's details and its Windows hardware error count.</summary>
internal sealed class CpuInfoForm : Form
{
    public CpuInfoForm(Translator t, HardwareInventory hardware, Font font)
    {
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["ui.cpuInfo"];
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(LogicalToDeviceUnits(560), LogicalToDeviceUnits(420));

        var rows = CpuSummary.Details(t, hardware).Select(line =>
        {
            var isAlert = line.Label == t["summary.whea"] && hardware.Reliability?.WheaErrors > 0;
            return new InfoRow(line.Label, line.Value, isAlert ? Ui.AlertColor : null);
        }).ToList();

        var table = new InfoView { Dock = DockStyle.Fill, Padding = new Padding(Ui.SPACING) };
        Controls.Add(table);
        table.SetRows(rows);
    }
}
