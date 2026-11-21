using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog editing a bookmark (F4 or Edit in the bookmark list): its description and path, and for a bookmark of
///     a volume provider asking for a location (e.g. SMB or FTP/SFTP), the location, user name and stored password.
///     Changing the location changes the path along with it until the path is edited. Invalid input is shown below the
///     fields and keeps the dialog open. The dialog returns the changes on OK or null on cancel.
/// </summary>
public partial class EditBookmarkDialog : Window
{
    private readonly Bookmark? _bookmark;

    // path set along with the location, to stop doing so once the user edits the path
    private string _locationPath = string.Empty;

    /// <summary>For the XAML previewer.</summary>
    public EditBookmarkDialog()
    {
        InitializeComponent();
    }

    /// <param name="bookmark">Bookmark to edit.</param>
    /// <param name="credentialStoreName">Name of the credential store the password is stored in.</param>
    public EditBookmarkDialog(Bookmark bookmark, string credentialStoreName) : this()
    {
        _bookmark = bookmark;
        _locationPath = bookmark.Path;
        var hasLocation = bookmark.Location != null;

        VolumeText.Text = bookmark.VolumeProviderName ?? "Local";
        DescriptionTextBox.Text = bookmark.Description;
        PathTextBox.Text = bookmark.Path;
        LocationLabel.IsVisible = LocationTextBox.IsVisible = hasLocation;
        UserNameLabel.IsVisible = UserNameTextBox.IsVisible = hasLocation;
        PasswordLabel.IsVisible = PasswordTextBox.IsVisible = StorePasswordCheckBox.IsVisible = hasLocation;
        LocationTextBox.Text = bookmark.Location ?? string.Empty;
        UserNameTextBox.Text = bookmark.UserName ?? string.Empty;
        PasswordTextBox.PlaceholderText = bookmark.HasPassword ? "Empty keeps the stored password" : "Not stored";
        StorePasswordCheckBox.Content = $"_Store the password in {credentialStoreName}";
        StorePasswordCheckBox.IsChecked = bookmark.HasPassword;
        LocationTextBox.TextChanged += (_, _) => UpdatePathFromLocation();

        OkButton.Click += (_, _) =>
        {
            var changes = new BookmarkChanges(DescriptionTextBox.Text ?? string.Empty,
                hasLocation ? LocationTextBox.Text : null, PathTextBox.Text ?? string.Empty,
                hasLocation ? UserNameTextBox.Text : null, hasLocation ? PasswordTextBox.Text : null,
                hasLocation && StorePasswordCheckBox.IsChecked == true);
            if (changes.GetError(bookmark) is { } error)
            {
                ErrorText.Text = error;
                ErrorText.IsVisible = true;
                return;
            }

            Close(changes);
        };
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            DescriptionTextBox.Focus();
            DescriptionTextBox.SelectAll();
        };
    }

    private void UpdatePathFromLocation()
    {
        if (_bookmark == null || PathTextBox.Text != _locationPath ||
            _bookmark.GetPathInLocation(LocationTextBox.Text ?? string.Empty) is not { } path)
        {
            return;
        }

        _locationPath = path;
        PathTextBox.Text = path;
    }
}
