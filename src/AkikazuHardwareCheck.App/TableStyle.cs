using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// One flat, system-coloured table for every list. It keeps the old list look while providing sorting, column
/// resizing/reordering, hiding and row copy.
/// </summary>
internal static class TableStyle
{
    public static DataGridView Create()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = true,
            GridColor = SystemColors.ControlLight,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        grid.DefaultCellStyle.BackColor = SystemColors.Window;
        grid.DefaultCellStyle.ForeColor = SystemColors.ControlText;
        grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
        grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        grid.RowTemplate.Height = grid.LogicalToDeviceUnits(26);
        return grid;
    }

    public static DataGridViewTextBoxColumn Column(string name, string header, int fillWeight, int minimumWidth)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            SortMode = DataGridViewColumnSortMode.Automatic,
        };
    }

    /// <summary>Right-click a header to hide or show columns. Sorting remains a normal header click.</summary>
    public static void EnableColumnMenu(DataGridView grid, Translator t)
    {
        var menu = new ContextMenuStrip();
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            menu.Items.Clear();
            foreach (var column in grid.Columns.Cast<DataGridViewColumn>())
            {
                var item = new ToolStripMenuItem(column.HeaderText) { Checked = column.Visible, CheckOnClick = true };
                item.Click += (_, _) =>
                {
                    if (!item.Checked && grid.Columns.Cast<DataGridViewColumn>().Count(candidate => candidate.Visible) <= 1)
                    {
                        item.Checked = true;
                        return;
                    }

                    column.Visible = item.Checked;
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            var showAll = new ToolStripMenuItem(t["advanced.showAllColumns"]);
            showAll.Click += (_, _) =>
            {
                foreach (var column in grid.Columns.Cast<DataGridViewColumn>())
                {
                    column.Visible = true;
                }
            };
            menu.Items.Add(showAll);
            menu.Show(grid, e.Location);
        };
    }

    public static void EnableCopy(DataGridView grid, Translator t)
    {
        var menu = new ContextMenuStrip();
        var copy = new ToolStripMenuItem(t["advanced.copy"]);
        copy.Click += (_, _) => Copy(grid, t);
        menu.Items.Add(copy);
        grid.ContextMenuStrip = menu;
        grid.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                Copy(grid, t);
                e.Handled = true;
            }
        };
    }

    public static void Copy(DataGridView grid, Translator t)
    {
        if (grid.CurrentRow is not { } row)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join('\t', row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value?.ToString() ?? "")));
        }
        catch (ExternalException)
        {
            // Another process can hold the clipboard; copying is best effort.
        }
    }
}
