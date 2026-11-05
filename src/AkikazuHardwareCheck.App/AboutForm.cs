using System.ComponentModel;
using System.Diagnostics;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.App;

/// <summary>Who makes the tool and where to reach them.</summary>
internal sealed class AboutForm : Form
{
    private const string EMAIL = "timkayhou@akikazupc.com";
    private const string WEBSITE = "https://www.akikazu.co.jp/";
    public const string REPOSITORY = "https://github.com/AkiCust-PC/hardware-check";

    public AboutForm(Translator t, Font font)
    {
        Font = font;
        Icon = Ui.AppIcon;
        Text = t["ui.about"].TrimEnd('…');
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var title = Ui.Label(isBold: true);
        title.Text = t["ui.appName"];

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(Ui.SPACING * 2) };
        table.Controls.Add(title);
        table.SetColumnSpan(title, 2);
        AddText(table, t["ui.aboutVersion"], CheckReport.CurrentToolVersion);
        AddText(table, t["ui.aboutCompany"], t["report.company"]);
        AddLink(table, t["ui.aboutEmail"], EMAIL, $"mailto:{EMAIL}");
        AddLink(table, t["ui.aboutWebsite"], WEBSITE, WEBSITE);
        AddLink(table, t["ui.aboutSource"], REPOSITORY, REPOSITORY);

        var licenses = Ui.Button();
        licenses.Text = t["ui.licenses"];
        licenses.Click += (_, _) => SettingsForm.OpenLicenses();
        var close = Ui.Button();
        close.Text = t["ui.ok"];
        close.DialogResult = DialogResult.OK;
        AcceptButton = close;
        CancelButton = close;
        var buttons = Ui.Flow();
        buttons.Margin = new Padding(0, Ui.SPACING, 0, 0);
        buttons.Controls.AddRange([close, licenses]);
        table.Controls.Add(buttons);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);
        Ui.ScaleButtons(this);
    }

    public static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // No program is set up for this kind of link, and the address is still on screen to copy.
            Trace.TraceWarning(exception.Message);
        }
    }

    private static void AddText(TableLayoutPanel table, string label, string value)
    {
        var name = Ui.Label();
        name.Text = label;
        name.ForeColor = SystemColors.GrayText;
        var text = Ui.Label();
        text.Text = value;
        table.Controls.Add(name);
        table.Controls.Add(text);
    }

    private static void AddLink(TableLayoutPanel table, string label, string text, string target)
    {
        var name = Ui.Label();
        name.Text = label;
        name.ForeColor = SystemColors.GrayText;
        var link = new LinkLabel { AutoSize = true, Text = text, Anchor = AnchorStyles.Left, Margin = new Padding(0, Ui.SPACING / 2, Ui.SPACING, Ui.SPACING / 2) };
        link.LinkClicked += (_, _) => OpenExternal(target);
        table.Controls.Add(name);
        table.Controls.Add(link);
    }
}
