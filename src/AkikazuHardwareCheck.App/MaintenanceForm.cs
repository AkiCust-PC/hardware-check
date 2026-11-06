using System.Diagnostics;
using System.Globalization;
using AkikazuHardwareCheck.Core.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Read-only startup, software and process lists, event summary, cleanup and system-tool shortcuts.</summary>
internal sealed class MaintenanceForm : Form
{
    private readonly Translator _t;
    private readonly ListView _startup = ListViewTable.Create();
    private readonly ListView _software = ListViewTable.Create();
    private readonly ListView _processes = ListViewTable.Create();
    private readonly ListView _events = ListViewTable.Create();
    private readonly CheckedListBox _cleanup = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly Button _scanCleanup = Ui.Button();
    private readonly Button _cleanCleanup = Ui.Button();
    private readonly List<JunkEntry> _junkEntries = [];
    private readonly Label _status = Ui.Label();

    public MaintenanceForm(Translator t, Font font)
    {
        _t = t;
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["maintenance.title"];
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(900), LogicalToDeviceUnits(620));
        MinimumSize = new Size(LogicalToDeviceUnits(760), LogicalToDeviceUnits(520));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildStartupPage());
        tabs.TabPages.Add(BuildSoftwarePage());
        tabs.TabPages.Add(BuildProcessPage());
        tabs.TabPages.Add(BuildEventPage());
        tabs.TabPages.Add(BuildCleanupPage());
        tabs.TabPages.Add(BuildToolsPage());

        var refresh = Ui.Button();
        refresh.Text = t["maintenance.refresh"];
        refresh.Click += async (_, _) => await LoadAsync();
        var close = Ui.Button();
        close.Text = t["ui.ok"];
        close.DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, Ui.SPACING, 0, 0) };
        buttons.Controls.Add(close);
        buttons.Controls.Add(refresh);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.SPACING) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(_status, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;

        _startup.Columns.Add(t["maintenance.name"], LogicalToDeviceUnits(180));
        _startup.Columns.Add(t["maintenance.command"], LogicalToDeviceUnits(420));
        _startup.Columns.Add(t["maintenance.source"], LogicalToDeviceUnits(180));
        _software.Columns.Add(t["maintenance.name"], LogicalToDeviceUnits(360));
        _software.Columns.Add(t["maintenance.version"], LogicalToDeviceUnits(120));
        _software.Columns.Add(t["maintenance.publisher"], LogicalToDeviceUnits(180));
        _software.Columns.Add(t["maintenance.installDate"], LogicalToDeviceUnits(130));
        _processes.Columns.Add(t["maintenance.name"], LogicalToDeviceUnits(260));
        _processes.Columns.Add(t["maintenance.pid"], LogicalToDeviceUnits(90));
        _processes.Columns.Add(t["maintenance.memory"], LogicalToDeviceUnits(140));
        _processes.Columns.Add(t["maintenance.publisher"], LogicalToDeviceUnits(180));
        _events.Columns.Add(t["maintenance.eventName"], LogicalToDeviceUnits(260));
        _events.Columns.Add(t["maintenance.eventCount"], LogicalToDeviceUnits(140));

        foreach (var list in new[] { _startup, _software, _processes, _events })
        {
            ListViewTable.EnableSort(list);
            ListViewTable.EnableColumnMenu(list, t);
            ListViewTable.EnableCopy(list, t);
        }

        Shown += async (_, _) => await LoadAsync();
        Ui.ScaleButtons(this);
    }

    private TabPage BuildStartupPage()
    {
        var page = new TabPage(_t["maintenance.startup"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var hint = new Label { AutoSize = true, Text = _t["maintenance.startupHint"], MaximumSize = new Size(LogicalToDeviceUnits(840), 0), Margin = new Padding(0, 0, 0, Ui.SPACING) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_startup, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSoftwarePage()
    {
        var page = new TabPage(_t["maintenance.software"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var uninstall = Ui.Button();
        uninstall.Text = _t["maintenance.uninstall"];
        uninstall.Click += (_, _) => UninstallSelected();
        var hint = new Label { AutoSize = true, Text = _t["maintenance.softwareHint"], MaximumSize = new Size(LogicalToDeviceUnits(840), 0), Margin = new Padding(0, 0, 0, Ui.SPACING) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_software, 0, 1);
        layout.Controls.Add(uninstall, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildProcessPage()
    {
        var page = new TabPage(_t["maintenance.processes"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var hint = new Label { AutoSize = true, Text = _t["maintenance.processHint"], MaximumSize = new Size(LogicalToDeviceUnits(840), 0), Margin = new Padding(0, 0, 0, Ui.SPACING) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_processes, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildEventPage()
    {
        var page = new TabPage(_t["maintenance.events"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var hint = new Label { AutoSize = true, Text = _t["maintenance.eventHint"], MaximumSize = new Size(LogicalToDeviceUnits(840), 0), Margin = new Padding(0, 0, 0, Ui.SPACING) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_events, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildCleanupPage()
    {
        var page = new TabPage(_t["maintenance.cleanup"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var hint = new Label { AutoSize = true, Text = _t["maintenance.cleanupHint"], MaximumSize = new Size(LogicalToDeviceUnits(840), 0), Margin = new Padding(0, 0, 0, Ui.SPACING) };
        _scanCleanup.Text = _t["maintenance.scan"];
        _scanCleanup.Click += async (_, _) => await ScanCleanupAsync();
        _cleanCleanup.Text = _t["maintenance.clean"];
        _cleanCleanup.Enabled = false;
        _cleanCleanup.Click += async (_, _) => await CleanCleanupAsync();
        var buttons = Ui.Flow();
        buttons.Controls.AddRange([_scanCleanup, _cleanCleanup]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_cleanup, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private async Task ScanCleanupAsync()
    {
        _cleanCleanup.Enabled = false;
        _status.Text = _t["maintenance.scanning"];
        var scan = await Task.Run(JunkCleaner.Scan);
        if (IsDisposed)
        {
            return;
        }

        _junkEntries.Clear();
        _cleanup.Items.Clear();
        foreach (var entry in scan.Entries)
        {
            _junkEntries.Add(entry);
            _cleanup.Items.Add($"{_t["junk." + entry.Category]} - {MaintenanceCollectors.FormatBytes(entry.Bytes)} ({entry.Files})", false);
        }

        _cleanCleanup.Enabled = _junkEntries.Count > 0;
        _status.Text = scan.IsAdministrator ? _t["maintenance.cleanupScanned"] : _t["maintenance.cleanupAdminHint"];
    }

    private async Task CleanCleanupAsync()
    {
        var selected = _cleanup.CheckedIndices.Cast<int>().ToList();
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
            var result = await Task.Run(() => JunkCleaner.Clean(_junkEntries[index].Category));
            deleted += result.DeletedFiles;
            bytes += result.DeletedBytes;
            skipped += result.SkippedFiles;
        }

        _status.Text = _t["maintenance.cleaned", deleted, MaintenanceCollectors.FormatBytes(bytes), skipped];
        await ScanCleanupAsync();
    }

    private TabPage BuildToolsPage()
    {
        var page = new TabPage(_t["maintenance.systemTools"]) { Padding = new Padding(Ui.SPACING), UseVisualStyleBackColor = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        AddTool(buttons, _t["maintenance.openEventViewer"], "eventvwr.exe");
        AddTool(buttons, _t["maintenance.openDeviceManager"], "devmgmt.msc");
        AddTool(buttons, _t["maintenance.openServices"], "services.msc");
        AddTool(buttons, _t["maintenance.openSystemInfo"], "msinfo32.exe");
        AddTool(buttons, _t["maintenance.openDxDiag"], "dxdiag.exe");
        page.Controls.Add(buttons);
        return page;
    }

    private void AddTool(Control parent, string text, string target)
    {
        var button = Ui.Button();
        button.Text = text;
        button.Click += (_, _) => OpenTool(target);
        parent.Controls.Add(button);
    }

    private async Task LoadAsync()
    {
        _status.Text = _t["maintenance.running"];
        _startup.Items.Clear();
        _software.Items.Clear();
        _processes.Items.Clear();
        _events.Items.Clear();

        List<StartupEntry> startup;
        List<InstalledProgram> software;
        List<RunningProcess> processes;
        List<EventSummary> events;
        try
        {
            startup = await Task.Run(MaintenanceCollectors.ReadStartup);
            software = await Task.Run(MaintenanceCollectors.ReadInstalledPrograms);
            processes = await Task.Run(MaintenanceCollectors.ReadProcesses);
            events = await Task.Run(EventSummaryCollector.Read);
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                _status.Text = _t["maintenance.loadFailed", exception.Message];
            }

            return;
        }

        if (IsDisposed)
        {
            return;
        }

        foreach (var item in startup)
        {
            _startup.Items.Add(new ListViewItem([item.Name, item.Command, item.Source]));
        }

        foreach (var item in software)
        {
            _software.Items.Add(new ListViewItem([item.Name, item.Version ?? "", item.Publisher ?? "", MaintenanceCollectors.FormatInstallDate(item.InstallDate)]) { Tag = item });
        }

        foreach (var item in processes)
        {
            _processes.Items.Add(new ListViewItem([item.Name, item.Id.ToString(CultureInfo.InvariantCulture), MaintenanceCollectors.FormatBytes(item.WorkingSetBytes), item.Company ?? ""]));
        }

        foreach (var item in events)
        {
            _events.Items.Add(new ListViewItem([item.Name, item.Count?.ToString(CultureInfo.InvariantCulture) ?? _t["maintenance.unavailable"]]));
        }

        _status.Text = _t["maintenance.loaded", _startup.Items.Count, _software.Items.Count, _processes.Items.Count];
    }

    private void UninstallSelected()
    {
        if (_software.SelectedItems.Count == 0 || _software.SelectedItems[0].Tag is not InstalledProgram program)
        {
            _status.Text = _t["maintenance.noSelection"];
            return;
        }

        if (string.IsNullOrWhiteSpace(program.UninstallCommand))
        {
            _status.Text = _t["maintenance.noUninstaller"];
            return;
        }

        if (MessageBox.Show(this, _t["maintenance.uninstallConfirm", program.Name], _t["maintenance.uninstall"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /s /c \"{program.UninstallCommand}\"") { UseShellExecute = false, CreateNoWindow = true });
            _status.Text = _t["maintenance.uninstallStarted", program.Name];
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _status.Text = _t["maintenance.openFailed", exception.Message];
        }
    }

    private void OpenTool(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _status.Text = _t["maintenance.openFailed", exception.Message];
        }
    }
}
