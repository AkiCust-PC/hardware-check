using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AkikazuHardwareCheck.Core.AdvancedInfo;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// The third information level: a tree of categories and a property list. It shows the snapshot passed in by the
/// main window and only rebuilds that snapshot when Refresh is pressed; it never opens a hardware source itself.
/// </summary>
internal sealed class AdvancedInfoForm : Form
{
    private const int DETAILS_HEIGHT = 118;
    private const int TREE_WIDTH = 280;
    private const int TREE_MIN_WIDTH = 180;

    private readonly Translator _t;
    private readonly Func<AdvancedSnapshot> _rebuild;
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1 };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, MultiSelect = false };
    private readonly TextBox _search = new() { Width = 220, Margin = new Padding(0, 0, Ui.SPACING, 0) };
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = true };
    private readonly Label _status = Ui.Label();
    private AdvancedSnapshot _snapshot;

    public AdvancedInfoForm(Translator t, AdvancedSnapshot snapshot, Func<AdvancedSnapshot> rebuild, Font font)
    {
        _t = t;
        _snapshot = snapshot;
        _rebuild = rebuild;
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["advanced.title"];
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(640));
        MinimumSize = new Size(LogicalToDeviceUnits(800), LogicalToDeviceUnits(520));

        _list.Columns.Add(t["advanced.attribute"], LogicalToDeviceUnits(170));
        _list.Columns.Add(t["advanced.value"], LogicalToDeviceUnits(260));
        _list.Columns.Add(t["advanced.status"], LogicalToDeviceUnits(90));
        _list.Columns.Add(t["advanced.source"], LogicalToDeviceUnits(120));
        _list.SelectedIndexChanged += (_, _) => ShowField();
        _tree.AfterSelect += (_, _) => ShowNode();
        _search.TextChanged += (_, _) => PopulateTree();

        Controls.Add(BuildLayout());
        Ui.ScaleButtons(this);
        PopulateTree();
    }

    private Control BuildLayout()
    {
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0) };
        var searchLabel = Ui.Label();
        searchLabel.Text = _t["advanced.search"];
        top.Controls.Add(searchLabel);
        top.Controls.Add(_search);

        AddButton(top, _t["advanced.refresh"], (_, _) => RefreshSnapshot());
        AddButton(top, _t["advanced.expandAll"], (_, _) => _tree.ExpandAll());
        AddButton(top, _t["advanced.collapseAll"], (_, _) => _tree.CollapseAll());
        AddButton(top, _t["advanced.copy"], (_, _) => CopySelected());
        AddButton(top, _t["advanced.export"], (_, _) => ExportSnapshot());

        _split.Panel1MinSize = LogicalToDeviceUnits(TREE_MIN_WIDTH);
        _split.Panel1.Controls.Add(_tree);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(DETAILS_HEIGHT)));
        right.Controls.Add(_list, 0, 0);
        right.Controls.Add(_details, 0, 1);
        _split.Panel2.Controls.Add(right);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.SPACING), Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(_split, 0, 1);
        root.Controls.Add(_status, 0, 2);
        return root;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // SplitContainer resets a distance assigned before its final width is known. Set it once the window is shown.
        var wanted = Math.Min(LogicalToDeviceUnits(TREE_WIDTH), Math.Max(LogicalToDeviceUnits(TREE_MIN_WIDTH), ClientSize.Width / 3));
        _split.SplitterDistance = wanted;
    }

    private void AddButton(Control parent, string text, EventHandler handler)
    {
        var button = Ui.Button();
        button.Text = text;
        button.MinimumSize = new Size(0, LogicalToDeviceUnits(Ui.BUTTON_MIN_HEIGHT));
        button.Click += handler;
        parent.Controls.Add(button);
    }

    private void RefreshSnapshot()
    {
        _snapshot = _rebuild();
        PopulateTree();
        SetStatus(null);
    }

    private void PopulateTree()
    {
        var query = _search.Text.Trim();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        foreach (var root in _snapshot.Roots.Where(node => Matches(node, query)))
        {
            _tree.Nodes.Add(CreateTreeNode(root, query));
        }

        _tree.ExpandAll();
        if (_tree.Nodes.Count > 0)
        {
            _tree.SelectedNode = _tree.Nodes[0];
        }
        else
        {
            _list.Items.Clear();
            _details.Text = _t["advanced.noSelection"];
        }

        _tree.EndUpdate();
    }

    private static TreeNode CreateTreeNode(InfoNode node, string query)
    {
        var treeNode = new TreeNode(node.Label) { Tag = node };
        foreach (var child in (node.Children ?? []).Where(item => Matches(item, query)))
        {
            treeNode.Nodes.Add(CreateTreeNode(child, query));
        }

        return treeNode;
    }

    private static bool Matches(InfoNode node, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        if (node.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return node.SelfAndDescendants().Any(item =>
            item.Groups.Any(group => group.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                || group.Fields.Any(field => Matches(field, query))));
    }

    private static bool Matches(InfoField field, string query) =>
        field.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (field.Value?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.RawValue?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.Note?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.Provenance?.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.Provenance?.Api.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private void ShowNode()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        if (_tree.SelectedNode?.Tag is InfoNode node)
        {
            foreach (var group in node.Groups)
            {
                _list.Items.Add(new ListViewItem(group.Label) { ForeColor = Ui.MutedTextColor });
                foreach (var field in group.Fields)
                {
                    var value = field.Value ?? StateText(field.State);
                    var item = new ListViewItem([field.Label, value, StateText(field.State), field.Provenance?.Source ?? ""]) { Tag = field };
                    if (field.State == InfoState.ReadFailed)
                    {
                        item.ForeColor = Ui.AlertColor;
                    }
                    else if (!field.HasValue)
                    {
                        item.ForeColor = Ui.MutedTextColor;
                    }

                    _list.Items.Add(item);
                }
            }
        }

        _list.EndUpdate();
        _details.Text = "";
    }

    private void ShowField()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not InfoField field)
        {
            _details.Text = _t["advanced.noSelection"];
            return;
        }

        var lines = new List<string>
        {
            $"{_t["advanced.attribute"]}: {field.Label}",
            $"{_t["advanced.value"]}: {field.Value ?? StateText(field.State)}",
            $"{_t["advanced.status"]}: {StateText(field.State)}",
            $"{_t["advanced.origin"]}: {OriginText(field.Origin)}",
        };
        if (field.RawValue is { } raw)
        {
            lines.Add($"{_t["advanced.rawValue"]}: {raw}");
        }

        if (field.Unit is { } unit)
        {
            lines.Add($"{_t["advanced.unit"]}: {unit}");
        }

        if (field.Provenance is { } provenance)
        {
            lines.Add($"{_t["advanced.source"]}: {provenance.Source} / {provenance.Api}");
            lines.Add($"{_t["advanced.requiresAdmin"]}: {YesNo(provenance.RequiresAdministrator)}");
            lines.Add($"{_t["advanced.requiresDriver"]}: {YesNo(provenance.RequiresDriver)}");
            lines.Add($"{_t["advanced.refreshable"]}: {YesNo(provenance.IsRefreshable)}");
        }

        if (field.Note is { } note)
        {
            lines.Add($"{_t["advanced.details"]}: {note}");
        }

        _details.Text = string.Join(Environment.NewLine, lines);
    }

    private void CopySelected()
    {
        var text = BuildText();
        try
        {
            Clipboard.SetText(text);
            SetStatus(_t["advanced.copied"]);
        }
        catch (Exception exception) when (exception is ExternalException or ThreadStateException)
        {
            SetStatus(exception.Message, isAlert: true);
        }
    }

    private string BuildText()
    {
        var builder = new StringBuilder();
        if (_tree.SelectedNode?.Tag is InfoNode node)
        {
            AppendNode(builder, node);
        }
        else
        {
            foreach (var root in _snapshot.Roots)
            {
                AppendNode(builder, root);
            }
        }

        return builder.ToString();
    }

    private void AppendNode(StringBuilder builder, InfoNode node)
    {
        builder.AppendLine(node.Label);
        foreach (var group in node.Groups)
        {
            builder.Append("  ").AppendLine(group.Label);
            foreach (var field in group.Fields)
            {
                builder.Append("    ").Append(field.Label).Append('\t').AppendLine(field.Value ?? StateText(field.State));
            }
        }

        foreach (var child in node.Children ?? [])
        {
            AppendNode(builder, child);
        }
    }

    private void ExportSnapshot()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = _t["advanced.exportFilter"],
            FileName = $"advanced-hardware-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (AdvancedInfoExporter.TrySave(_snapshot, dialog.FileName) is { } failure)
        {
            SetStatus(_t["advanced.exportFailed", failure], isAlert: true);
            return;
        }

        SetStatus(_t["advanced.exported", dialog.FileName]);
    }

    private string StateText(InfoState state) => _t[EnumText.Key("advanced.state", state)];

    private string OriginText(InfoOrigin origin) => _t[EnumText.Key("advanced.origin", origin)];

    private string YesNo(bool value) => _t[value ? "common.yes" : "common.no"];

    private void SetStatus(string? text, bool isAlert = false)
    {
        _status.Text = text ?? "";
        _status.ForeColor = isAlert ? Ui.AlertColor : SystemColors.ControlText;
    }
}
