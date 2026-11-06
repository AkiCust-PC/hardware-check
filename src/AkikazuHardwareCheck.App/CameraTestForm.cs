using System.Diagnostics;
using System.Management;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Lists camera devices and opens Windows Camera for a live preview; no capture is stored.</summary>
internal sealed class CameraTestForm : Form
{
    private readonly Translator _t;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly Label _status = Ui.Label();

    public CameraTestForm(Translator t, Font font)
    {
        _t = t;
        Text = t["cameraTest.title"];
        Icon = Ui.AppIcon;
        Font = font;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(620), LogicalToDeviceUnits(360));
        MinimumSize = new Size(LogicalToDeviceUnits(520), LogicalToDeviceUnits(300));

        _list.Columns.Add(t["cameraTest.name"], LogicalToDeviceUnits(420));
        _list.Columns.Add(t["cameraTest.class"], LogicalToDeviceUnits(140));
        var refresh = Ui.Button();
        refresh.Text = t["ui.refresh"];
        refresh.Click += (_, _) => LoadDevices();
        var open = Ui.Button();
        open.Text = t["cameraTest.open"];
        open.Click += (_, _) => OpenCamera();
        var buttons = Ui.Flow();
        buttons.Controls.AddRange([refresh, open]);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.SPACING) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_list, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        Ui.ScaleButtons(this);
        Shown += (_, _) => LoadDevices();
    }

    private void LoadDevices()
    {
        _list.Items.Clear();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
            foreach (var device in searcher.Get().Cast<ManagementObject>())
            {
                var item = new ListViewItem(device["Name"]?.ToString() ?? _t["cameraTest.unknown"]);
                item.SubItems.Add(device["PNPClass"]?.ToString() ?? string.Empty);
                _list.Items.Add(item);
            }

            _status.Text = _t["cameraTest.count", _list.Items.Count];
        }
        catch (ManagementException exception)
        {
            _status.Text = _t["cameraTest.failed", exception.Message];
        }
    }

    private void OpenCamera()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-camera:") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _status.Text = _t["cameraTest.openFailed", exception.Message];
        }
    }
}
