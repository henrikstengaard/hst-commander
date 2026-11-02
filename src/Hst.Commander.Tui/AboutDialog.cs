using System.Collections.ObjectModel;
using System.Text;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     About dialog (Help > About): the name, version, build date, author and a short description of Hst Commander, and
///     the Licenses section listing the license notices (<see cref="Licenses.GetNotices" />) of Hst Commander, its
///     third-party components and the loaded plugins. Enter, double-clicking a notice or View license shows its text.
/// </summary>
public sealed class AboutDialog : Dialog
{
    private const int MaximumWidth = 72;
    private const int MaximumListHeight = 8;

    private readonly TuiPalette _palette;
    private readonly IReadOnlyList<LicenseNotice> _notices;
    private readonly ListView _listView;

    public AboutDialog(TuiPalette palette)
    {
        _palette = palette;
        _notices = Licenses.GetNotices();
        var appInfo = AppInfo.Current;
        Title = "About";
        ColorScheme = palette.DialogColorScheme;

        // the texts are wrapped to the dialog's width, so the labels are as high as their lines
        var width = Math.Max(Math.Min(MaximumWidth, Application.Screen.Width - 2), 30);
        // less the borders, the margins of the labels, the padding and the shadow of the dialog
        var textWidth = width - 6;
        var y = 1;
        var nameLabel = new Label { X = 1, Y = y++, Text = appInfo.NameAndVersion };
        Add(nameLabel);
        if (appInfo.BuildDateText.Length > 0)
        {
            Add(new Label { X = 1, Y = y++, Text = appInfo.BuildDateText });
        }

        Add(new Label { X = 1, Y = y++, Text = $"Created by {appInfo.Author}" });
        if (appInfo.Description.Length > 0)
        {
            y = AddWrappedText(appInfo.Description, y + 1, textWidth);
        }

        Add(new Label { X = 1, Y = ++y, Text = "Licenses:" });
        y = AddWrappedText(Licenses.Summary, y + 1, textWidth);
        // the list scrolls, so it's lower on small screens (e.g. 80x24 with plugin notices) for the dialog to fit
        var listHeight = Math.Max(Math.Min(Math.Min(_notices.Count, MaximumListHeight),
            Application.Screen.Height - (y + 1 + 5)), 2);
        _listView = new ListView { X = 1, Y = y + 1, Width = Dim.Fill(1), Height = listHeight };
        _listView.SetSource(new ObservableCollection<string>(_notices.Select(notice => notice.Title)));
        // Enter and double-clicking run the list's accept command, which would else accept the dialog's default
        // button (OK) and close it
        _listView.Accepting += (_, e) =>
        {
            e.Cancel = true;
            ShowNotice(_listView.SelectedItem);
        };
        Add(_listView);
        Width = width;
        Height = y + 1 + listHeight + 5;

        var viewButton = new Button { Text = "_View license" };
        viewButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            ShowNotice(_listView.SelectedItem);
        };
        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        AddButton(viewButton);
        AddButton(okButton);
        Initialized += (_, _) => okButton.SetFocus();
    }

    /// <summary>Adds the text wrapped to the width at the row, returning the row after it.</summary>
    private int AddWrappedText(string text, int y, int width)
    {
        var lines = WrapText(text, width);
        Add(new Label { X = 1, Y = y, Width = Dim.Fill(1), Height = lines.Count, Text = string.Join("\n", lines) });
        return y + lines.Count;
    }

    /// <summary>Wraps the text at spaces into lines of at most the width (longer words are split).</summary>
    internal static IReadOnlyList<string> WrapText(string text, int width)
    {
        List<string> lines = [];
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            while (rest.Length > 0)
            {
                var needed = line.Length == 0 ? rest.Length : line.Length + 1 + rest.Length;
                if (needed <= width)
                {
                    if (line.Length > 0)
                    {
                        line.Append(' ');
                    }

                    line.Append(rest);
                    rest = string.Empty;
                }
                else if (line.Length > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                else
                {
                    lines.Add(rest[..width]);
                    rest = rest[width..];
                }
            }
        }

        if (line.Length > 0 || lines.Count == 0)
        {
            lines.Add(line.ToString());
        }

        return lines;
    }

    private void ShowNotice(int index)
    {
        if (index < 0 || index >= _notices.Count)
        {
            return;
        }

        var dialog = new LicenseDialog(_notices[index], _palette);
        Application.Run(dialog);
        dialog.Dispose();
        _listView.SetFocus();
    }
}

/// <summary>Shows the text of a license notice from the About dialog in a read-only text view.</summary>
public sealed class LicenseDialog : Dialog
{
    public LicenseDialog(LicenseNotice notice, TuiPalette palette)
    {
        Title = notice.Title;
        Width = Dim.Percent(90);
        Height = Dim.Percent(90);
        ColorScheme = palette.DialogColorScheme;

        var textView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            Text = notice.Text.Replace("\r\n", "\n")
        };

        var closeButton = new Button { Text = "Close", IsDefault = true };
        closeButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(textView);
        AddButton(closeButton);
        Initialized += (_, _) => textView.SetFocus();
    }
}
