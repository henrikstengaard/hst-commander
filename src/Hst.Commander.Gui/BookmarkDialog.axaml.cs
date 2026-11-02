using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the description of a bookmark, when adding one (Ctrl+B) or renaming one (F2 in the bookmark
///     list), showing the bookmarked location. The dialog returns the description on OK or null on cancel.
/// </summary>
public partial class BookmarkDialog : Window
{
    public BookmarkDialog() : this("Add bookmark", string.Empty, string.Empty)
    {
    }

    /// <param name="title">Title, e.g. "Add bookmark".</param>
    /// <param name="location">Location bookmarked, e.g. "c:\temp".</param>
    /// <param name="description">Description shown initially.</param>
    public BookmarkDialog(string title, string location, string description)
    {
        InitializeComponent();
        Title = title;
        LocationText.Text = location;
        ToolTip.SetTip(LocationText, location);
        DescriptionTextBox.Text = description;

        OkButton.Click += (_, _) =>
        {
            var text = DescriptionTextBox.Text?.Trim() ?? string.Empty;
            if (text.Length > 0)
            {
                Close(text);
            }
        };
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            DescriptionTextBox.Focus();
            DescriptionTextBox.SelectAll();
        };
    }
}
