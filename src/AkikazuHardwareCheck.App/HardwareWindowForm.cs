using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Hosts the existing hardware information panel in a separate window without duplicating its controls.</summary>
internal sealed class HardwareWindowForm : Form
{
    public HardwareWindowForm(Translator t, Control hardware, Font font)
    {
        Text = t["ui.hardwareWindow"];
        Icon = Ui.AppIcon;
        Font = font;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(480));
        Size = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(720));
        ShowInTaskbar = true;
        hardware.Dock = DockStyle.Fill;
        hardware.Margin = new Padding(0);
        Controls.Add(hardware);
    }
}
