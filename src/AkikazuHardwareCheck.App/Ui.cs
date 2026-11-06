using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Shared control settings. Every control here is a standard system control with its default look, so Windows
/// itself draws the style of its own version and follows its light or dark setting.
/// </summary>
internal static class Ui
{
    private const double BYTES_PER_KB = 1000;
    private const double BYTES_PER_MB = 1000 * 1000;

    public const int SPACING = 8;
    public const int BUTTON_MIN_WIDTH = 120;
    public const int BUTTON_MIN_HEIGHT = 32;
    public const int PERCENT_MAX = 100;

    /// <summary>Status colors carry meaning that system colors do not, so they are chosen to stay readable on either theme.</summary>
    public static Color AlertColor => Application.IsDarkModeEnabled ? Color.FromArgb(255, 153, 164) : Color.FromArgb(179, 38, 30);

    public static Color WarningColor => Application.IsDarkModeEnabled ? Color.FromArgb(252, 225, 0) : Color.FromArgb(146, 84, 0);

    public static Color SuccessColor => Application.IsDarkModeEnabled ? Color.FromArgb(108, 203, 95) : Color.FromArgb(27, 110, 60);

    /// <summary>Set while the window has a chosen color or picture, where the dim system text color may be unreadable.</summary>
    public static bool HasCustomBackground { get; set; }

    public static Color MutedTextColor => HasCustomBackground ? SystemColors.ControlText : SystemColors.GrayText;

    /// <summary>
    /// The icon embedded in this assembly, so every window keeps it in single-file builds and in test hosts whose own
    /// executable has no icon. Falls back to the executable's icon if the resource is ever missing.
    /// </summary>
    public static Icon? AppIcon { get; } = LoadAppIcon();

    private static Icon? LoadAppIcon()
    {
        using var resource = typeof(Ui).Assembly.GetManifestResourceStream("app.ico");
        if (resource is not null)
        {
            return new Icon(resource);
        }

        try
        {
            return Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Han characters differ by region, so each language uses the Windows font drawn for it, at the system's size.</summary>
    public static Font FontFor(string language)
    {
        var system = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
        var family = language switch
        {
            AppLanguage.JAPANESE => "Yu Gothic UI",
            AppLanguage.SIMPLIFIED_CHINESE => "Microsoft YaHei UI",
            AppLanguage.TRADITIONAL_CHINESE => "Microsoft JhengHei UI",
            _ => null,
        };

        if (family is null)
        {
            return system;
        }

        // Asking for the font directly works on any display language, where installed fonts may list under local names.
        // Characters a font lacks still display through Windows font linking, so a missing font only changes their style.
        try
        {
            using var installed = new FontFamily(family);
            return new Font(installed, system.Size);
        }
        catch (ArgumentException)
        {
            return system;
        }
    }

    public static string FormatRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is not { } rate)
        {
            return "—";
        }

        return rate >= BYTES_PER_MB
            ? $"{(rate / BYTES_PER_MB).ToString("0.0", CultureInfo.InvariantCulture)} MB/s"
            : $"{(rate / BYTES_PER_KB).ToString("0", CultureInfo.InvariantCulture)} KB/s";
    }

    public static Button Button()
    {
        return new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(BUTTON_MIN_WIDTH, BUTTON_MIN_HEIGHT),
            Margin = new Padding(0, 0, SPACING, SPACING),
            UseVisualStyleBackColor = true,
        };
    }

    /// <summary>Windows' own MDL2 icon font; no icon package or image assets are needed.</summary>
    /// <summary>Shared native progress-bar setup for every metric tile.</summary>
    public static ProgressBar MetricBar() => new()
    {
        Maximum = PERCENT_MAX,
        Dock = DockStyle.Top,
        Margin = new Padding(0),
        Visible = true,
        Value = 0,
    };

    public static Label IconLabel(string glyph, float size = 12f)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = glyph,
            ForeColor = SystemColors.Highlight,
            Margin = new Padding(0, 0, SPACING / 2, 0),
            Anchor = AnchorStyles.Left,
        };
        try
        {
            label.Font = new Font("Segoe MDL2 Assets", size);
        }
        catch (ArgumentException)
        {
            label.Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? Control.DefaultFont.FontFamily, size);
        }

        return label;
    }

    public static Label Label(bool isBold = false)
    {
        var label = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, SPACING / 2, SPACING, SPACING / 2),
        };
        if (isBold)
        {
            label.Font = new Font(label.Font, FontStyle.Bold);
        }

        return label;
    }

    public static GroupBox Group(Control content)
    {
        content.Dock = DockStyle.Fill;
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(SPACING),
            Margin = new Padding(0, 0, SPACING, SPACING),
        };
        group.Controls.Add(content);
        return group;
    }

    /// <summary>A system group box whose first row holds an optional control at the right, above the content.</summary>
    public static GroupBox Section(Control? headerRight, Control content)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (headerRight is not null)
        {
            headerRight.Anchor = AnchorStyles.Right;
            layout.Controls.Add(headerRight, 0, 0);
        }

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0);
        layout.Controls.Add(content, 0, 1);
        var group = Group(layout);
        group.AutoSize = false;
        return group;
    }

    public static FlowLayoutPanel Flow()
    {
        return new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0),
        };
    }

    /// <summary>Equal-width buttons in rows. Unlike a wrapping flow panel, its height does not depend on a width guess.</summary>
    public static TableLayoutPanel ButtonGrid(int columns, params Button[] buttons)
    {
        var grid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = columns, Margin = new Padding(0) };
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }

        foreach (var button in buttons)
        {
            button.AutoSize = false;
            button.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            button.MinimumSize = new Size(0, BUTTON_MIN_HEIGHT);
            grid.Controls.Add(button);
        }

        return grid;
    }

    /// <summary>Applies the logical minimum button size at the window's real DPI.</summary>
    public static void ScaleButtons(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button button)
            {
                button.MinimumSize = new Size(button.MinimumSize.Width == 0 ? 0 : button.LogicalToDeviceUnits(BUTTON_MIN_WIDTH), button.LogicalToDeviceUnits(BUTTON_MIN_HEIGHT));
                button.Height = button.MinimumSize.Height;
            }

            ScaleButtons(child);
        }
    }

    public static ComboBox Combo()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, SPACING, SPACING),
        };
    }
}
