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
    private readonly DataGridView _grid = TableStyle.Create();
    private readonly TextBox _search = new() { Width = 220, Margin = new Padding(0, 0, Ui.SPACING, 0) };
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = true };
    private readonly Label _status = Ui.Label();
    private readonly IReadOnlyList<Translator> _searchLanguages;
    private readonly Dictionary<string, List<string>> _labelKeys;
    private AdvancedSnapshot _snapshot;

    public AdvancedInfoForm(Translator t, AdvancedSnapshot snapshot, Func<AdvancedSnapshot> rebuild, Font font)
    {
        _t = t;
        _snapshot = snapshot;
        _rebuild = rebuild;
        _searchLanguages = AppLanguage.Supported.Select(Translator.Load).ToList();
        _labelKeys = BuildLabelIndex(_searchLanguages);
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["advanced.title"];
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(640));
        MinimumSize = new Size(LogicalToDeviceUnits(800), LogicalToDeviceUnits(520));

        var categoryColumn = TableStyle.Column("category", t["advanced.category"], 12, LogicalToDeviceUnits(80));
        var groupColumn = TableStyle.Column("group", t["advanced.group"], 16, LogicalToDeviceUnits(90));
        _grid.Columns.Add(categoryColumn);
        _grid.Columns.Add(groupColumn);
        _grid.Columns.Add(TableStyle.Column("property", t["advanced.attribute"], 24, LogicalToDeviceUnits(120)));
        _grid.Columns.Add(TableStyle.Column("value", t["advanced.value"], 28, LogicalToDeviceUnits(140)));
        _grid.Columns.Add(TableStyle.Column("status", t["advanced.status"], 10, LogicalToDeviceUnits(70)));
        _grid.Columns.Add(TableStyle.Column("source", t["advanced.source"], 10, LogicalToDeviceUnits(70)));
        categoryColumn.Visible = false;
        groupColumn.Visible = false;
        TableStyle.EnableColumnMenu(_grid, t);
        TableStyle.EnableCopy(_grid, t);
        _grid.SelectionChanged += (_, _) => ShowField();
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
        AddButton(top, _t["advanced.copy"], (_, _) => CopySelected());
        AddButton(top, _t["advanced.export"], (_, _) => ExportSnapshot());

        _split.Panel1MinSize = LogicalToDeviceUnits(TREE_MIN_WIDTH);
        _split.Panel1.Controls.Add(_tree);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(DETAILS_HEIGHT)));
        right.Controls.Add(_grid, 0, 0);
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
            _grid.Rows.Clear();
            _details.Text = _t["advanced.noSelection"];
        }

        _tree.EndUpdate();
    }

    private TreeNode CreateTreeNode(InfoNode node, string query)
    {
        var treeNode = new TreeNode(node.Label) { Tag = node };
        foreach (var child in (node.Children ?? []).Where(item => Matches(item, query)))
        {
            treeNode.Nodes.Add(CreateTreeNode(child, query));
        }

        return treeNode;
    }

    private bool Matches(InfoNode node, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        if (MatchesLabel(node.Label, query))
        {
            return true;
        }

        return node.SelfAndDescendants().Any(item =>
            item.Groups.Any(group => MatchesLabel(group.Label, query)
                || group.Fields.Any(field => Matches(field, query))));
    }

    private bool Matches(InfoField field, string query) =>
        MatchesLabel(field.Label, query)
        || field.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (field.Value?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.RawValue?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.Note?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.Provenance?.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (field.Provenance?.Api.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>The displayed label is enough to find its localization key; all four languages are then searched.</summary>
    private bool MatchesLabel(string label, string query)
    {
        if (label.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _labelKeys.TryGetValue(label, out var keys)
            && keys.Any(key => _searchLanguages.Any(language => language[key].Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private static Dictionary<string, List<string>> BuildLabelIndex(IReadOnlyList<Translator> languages)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in languages)
        {
            foreach (var key in language.Keys)
            {
                var text = language[key];
                if (text.Contains('{'))
                {
                    continue;
                }

                if (!index.TryGetValue(text, out var keys))
                {
                    keys = [];
                    index[text] = keys;
                }

                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        return index;
    }

    private void ShowNode()
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        if (_tree.SelectedNode?.Tag is InfoNode node)
        {
            foreach (var group in node.Groups)
            {
                foreach (var field in group.Fields)
                {
                    var value = field.Value ?? StateText(field.State);
                    var index = _grid.Rows.Add(node.Label, group.Label, field.Label, value, StateText(field.State), field.Provenance?.Source ?? "");
                    var row = _grid.Rows[index];
                    row.Tag = field;
                    if (field.State == InfoState.ReadFailed)
                    {
                        row.DefaultCellStyle.ForeColor = Ui.AlertColor;
                    }
                    else if (!field.HasValue)
                    {
                        row.DefaultCellStyle.ForeColor = Ui.MutedTextColor;
                    }
                }
            }
        }

        _grid.ResumeLayout();
        _details.Text = "";
    }

    private void ShowField()
    {
        var field = _grid.CurrentRow?.Tag as InfoField;
        if (field is null)
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
