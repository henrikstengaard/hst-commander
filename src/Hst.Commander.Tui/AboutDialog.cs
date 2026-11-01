using System.Collections.ObjectModel;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     About dialog (Help > About): the name, version and author, and the Licenses section listing the license notices
///     (<see cref="Licenses.GetNotices" />) of Hst Commander, its third-party components and the loaded plugins. Enter
///     or View license shows the notice under the cursor.
/// </summary>
public sealed class AboutDialog : Dialog
{
    private readonly TuiPalette _palette;
    private readonly IReadOnlyList<LicenseNotice> _notices;
    private readonly ListView _listView;

    public AboutDialog(TuiPalette palette)
    {
        _palette = palette;
        _notices = Licenses.GetNotices();
        var appInfo = AppInfo.Current;
        Title = "About";
        Width = Dim.Percent(60);
        Height = Math.Min(_notices.Count, 8) + 13;
        ColorScheme = palette.DialogColorScheme;

        var nameLabel = new Label { X = 1, Y = 1, Text = appInfo.NameAndVersion };
        var authorLabel = new Label { X = 1, Y = 2, Text = $"Created by {appInfo.Author}" };
        var licensesLabel = new Label { X = 1, Y = 4, Text = "Licenses:" };
        var summaryLabel = new Label { X = 1, Y = 5, Width = Dim.Fill(1), Height = 2, Text = Licenses.Summary };
        _listView = new ListView { X = 1, Y = 8, Width = Dim.Fill(1), Height = Dim.Fill(3) };
        _listView.SetSource(new ObservableCollection<string>(_notices.Select(notice => notice.Title)));
        _listView.OpenSelectedItem += (_, e) => ShowNotice(e.Item);

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

        Add(nameLabel, authorLabel, licensesLabel, summaryLabel, _listView);
        AddButton(viewButton);
        AddButton(okButton);
        Initialized += (_, _) => okButton.SetFocus();
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
