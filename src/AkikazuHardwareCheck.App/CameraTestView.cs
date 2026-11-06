using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Lists camera devices and opens Windows Camera for a live preview; no capture is stored.</summary>
internal sealed class CameraTestView : UserControl
{
    private const string CAMERA_URI = "microsoft.windows.camera:";
    private const string CAMERA_APP = @"shell:AppsFolder\Microsoft.WindowsCamera_8wekyb3d8bbwe!App";

    private readonly Translator _t;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly Label _status = Ui.Label();

    public CameraTestView(Translator t, Font font)
    {
        _t = t;
        Font = font;
        Dock = DockStyle.Fill;
        Padding = new Padding(Ui.SPACING);

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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_list, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        Load += (_, _) => LoadDevices();
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
        if (TryStart(CAMERA_URI) || TryStart(CAMERA_APP))
        {
            _status.Text = _t["cameraTest.opened"];
            return;
        }

        _status.Text = _t["cameraTest.openFailed", CAMERA_URI];
    }

    private static bool TryStart(string target)
    {
        try
        {
            using var process = target == CAMERA_APP
                ? Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true })
                : Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
