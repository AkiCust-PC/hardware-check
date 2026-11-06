using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Hosts the existing test controls and result panel in a separate modeless window.</summary>
internal sealed class TestWorkspaceForm : Form
{
    public TestWorkspaceForm(Translator t, Control? controls, Control results, Font font)
    {
        Text = controls is null ? t["ui.resultWindow"] : t["ui.testWindow"];
        Icon = Ui.AppIcon;
        Font = font;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(480));
        Size = new Size(LogicalToDeviceUnits(900), LogicalToDeviceUnits(650));
        ShowInTaskbar = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(Ui.SPACING) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, controls is null ? 0 : LogicalToDeviceUnits(240)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (controls is not null)
        {
            controls.Dock = DockStyle.Fill;
            controls.Margin = new Padding(0, 0, 0, Ui.SPACING);
            layout.Controls.Add(controls, 0, 0);
        }

        results.Dock = DockStyle.Fill;
        results.Margin = new Padding(0);
        layout.Controls.Add(results, 0, 1);
        Controls.Add(layout);
    }
}
