using System.Globalization;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Native Win32 list-view helpers. Sorting, resizing and reordering stay inside the system control; hiding a column
/// is only its width set to zero, never custom drawing.
/// </summary>
internal static class ListViewTable
{
    private const int LVM_FIRST = 0x1000;
    private const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
    private const int LVS_EX_DOUBLEBUFFER = 0x00010000;
    private const int LVS_EX_HEADERDRAGDROP = 0x00000010;

    public static ListView Create()
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false,
            AllowColumnReorder = true,
            ShowItemToolTips = true,
        };
        list.HandleCreated += (_, _) => SetExtendedStyles(list);
        return list;
    }

    public static void EnableSort(ListView list, Func<ListViewItem, int, IComparable?>? value = null)
    {
        list.ColumnClick += (_, e) =>
        {
            if (list.Columns.Count == 0 || list.Items.Count == 0)
            {
                return;
            }

            var ascending = list.Tag is not SortState state || state.Column != e.Column || !state.Ascending;
            list.Tag = new SortState(e.Column, ascending);
            list.ListViewItemSorter = new ItemComparer(e.Column, ascending, value);
            list.Sort();
        };
    }

    /// <summary>Right-click a column header to hide it. A hidden column keeps its width so it can be restored.</summary>
    public static void EnableColumnMenu(ListView list, Translator t)
    {
        var hiddenWidths = new Dictionary<ColumnHeader, int>();
        var menu = new ContextMenuStrip();
        list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            menu.Items.Clear();
            foreach (var column in list.Columns.Cast<ColumnHeader>())
            {
                var item = new ToolStripMenuItem(column.Text) { Checked = column.Width > 0, CheckOnClick = true };
                item.Click += (_, _) =>
                {
                    if (item.Checked)
                    {
                        column.Width = hiddenWidths.Remove(column, out var width) ? width : 120;
                    }
                    else if (list.Columns.Cast<ColumnHeader>().Count(candidate => candidate.Width > 0) > 1)
                    {
                        hiddenWidths[column] = column.Width;
                        column.Width = 0;
                    }
                    else
                    {
                        item.Checked = true;
                    }
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            var showAll = new ToolStripMenuItem(t["advanced.showAllColumns"]);
            showAll.Click += (_, _) =>
            {
                foreach (var column in list.Columns.Cast<ColumnHeader>())
                {
                    if (column.Width == 0 && hiddenWidths.Remove(column, out var width))
                    {
                        column.Width = width;
                    }
                }
            };
            menu.Items.Add(showAll);
            menu.Show(list, e.Location);
        };
    }

    public static void EnableCopy(ListView list, Translator t)
    {
        var menu = new ContextMenuStrip();
        var copy = new ToolStripMenuItem(t["advanced.copy"]);
        copy.Click += (_, _) => Copy(list, t);
        menu.Items.Add(copy);
        list.ContextMenuStrip = menu;
        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                Copy(list, t);
                e.Handled = true;
            }
        };
    }

    public static void Copy(ListView list, Translator t)
    {
        if (list.SelectedItems.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join('\t', list.SelectedItems[0].SubItems.Cast<ListViewItem.ListViewSubItem>().Select(item => item.Text)));
        }
        catch (ExternalException)
        {
            // Clipboard ownership is best effort.
        }
    }

    private static void SetExtendedStyles(ListView list)
    {
        var styles = (IntPtr)(LVS_EX_DOUBLEBUFFER | LVS_EX_HEADERDRAGDROP);
        SendMessage(list.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, styles, styles);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private sealed record SortState(int Column, bool Ascending);

    private sealed class ItemComparer : System.Collections.IComparer
    {
        private readonly int _column;
        private readonly bool _ascending;
        private readonly Func<ListViewItem, int, IComparable?>? _value;

        public ItemComparer(int column, bool ascending, Func<ListViewItem, int, IComparable?>? value)
        {
            _column = column;
            _ascending = ascending;
            _value = value;
        }

        public int Compare(object? x, object? y)
        {
            if (x is not ListViewItem left || y is not ListViewItem right)
            {
                return 0;
            }

            var leftValue = _value?.Invoke(left, _column) ?? left.SubItems[_column].Text;
            var rightValue = _value?.Invoke(right, _column) ?? right.SubItems[_column].Text;
            var result = CompareValues(leftValue, rightValue);
            return _ascending ? result : -result;
        }

        private static int CompareValues(IComparable? left, IComparable? right)
        {
            if (left is null)
            {
                return right is null ? 0 : -1;
            }

            if (right is null)
            {
                return 1;
            }

            if (left is string leftText && right is string rightText
                && double.TryParse(leftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber)
                && double.TryParse(rightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            return left.CompareTo(right);
        }
    }
}
