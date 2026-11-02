using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the description of a bookmark, when adding one (Ctrl+B) or renaming one (F2 in the bookmark
///     list), showing the bookmarked location. <see cref="Result" /> holds the description on OK or null on
///     cancel or when empty.
/// </summary>
public sealed class BookmarkDialog : Dialog
{
    private readonly TextField _descriptionField;

    /// <param name="title">Title, e.g. "Add bookmark".</param>
    /// <param name="location">Location bookmarked, e.g. "c:\temp".</param>
    /// <param name="description">Description shown initially.</param>
    /// <param name="palette">Palette of the theme.</param>
    public BookmarkDialog(string title, string location, string description, TuiPalette palette)
    {
        Title = title;
        Width = Dim.Percent(60);
        Height = 10;
        ColorScheme = palette.DialogColorScheme;

        var locationLabel = new Label { X = 1, Y = 1, Text = "Location:" };
        var locationValue = new Label { X = 1, Y = 2, Width = Dim.Fill(1), Text = location };
        var descriptionLabel = new Label { X = 1, Y = 4, Text = "Description:" };
        _descriptionField = new TextField { X = 1, Y = 5, Width = Dim.Fill(1), Text = description };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            var text = _descriptionField.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            Result = text;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(locationLabel, locationValue, descriptionLabel, _descriptionField);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _descriptionField.SetFocus();
    }

    /// <summary>Description entered on OK, otherwise null.</summary>
    public string? Result { get; private set; }
}
