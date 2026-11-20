using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog editing a bookmark (F4 or Edit in the bookmark list): its description and path, and for a bookmark of
///     a volume provider asking for a location (e.g. SMB or FTP/SFTP), the location, user name and stored password.
///     Changing the location changes the path along with it until the path is edited. Invalid input is shown below the
///     fields and keeps the dialog open. <see cref="Result" /> holds the changes on OK or null on cancel.
/// </summary>
public sealed class EditBookmarkDialog : Dialog
{
    private const int FieldX = 14;

    private readonly Bookmark _bookmark;
    private readonly TextField _descriptionField;
    private readonly TextField? _locationField;
    private readonly TextField _pathField;
    private readonly TextField? _userNameField;
    private readonly TextField? _passwordField;
    private readonly CheckBox? _storePasswordCheckBox;
    private readonly Label _errorLabel;

    // path set along with the location, to stop doing so once the user edits the path
    private string _locationPath;

    /// <param name="bookmark">Bookmark to edit.</param>
    /// <param name="credentialStoreName">Name of the credential store the password is stored in.</param>
    /// <param name="palette">Palette of the theme.</param>
    public EditBookmarkDialog(Bookmark bookmark, string credentialStoreName, TuiPalette palette)
    {
        _bookmark = bookmark;
        _locationPath = bookmark.Path;
        Title = "Edit bookmark";
        Width = Dim.Percent(60);
        ColorScheme = palette.DialogColorScheme;

        var y = 1;
        var volumeLabel = new Label { X = 1, Y = y, Text = "Volume:" };
        var volumeValue = new Label
        {
            X = FieldX, Y = y, Width = Dim.Fill(1), Text = bookmark.VolumeProviderName ?? "Local"
        };
        Add(volumeLabel, volumeValue);
        y += 2;

        var descriptionLabel = new Label { X = 1, Y = y, Text = "Description:" };
        _descriptionField = new TextField { X = FieldX, Y = y, Width = Dim.Fill(1), Text = bookmark.Description };
        Add(descriptionLabel, _descriptionField);
        y++;

        if (bookmark.Location != null)
        {
            var locationLabel = new Label { X = 1, Y = y, Text = "Location:" };
            _locationField = new TextField { X = FieldX, Y = y, Width = Dim.Fill(1), Text = bookmark.Location };
            _locationField.TextChanged += (_, _) => UpdatePathFromLocation();
            Add(locationLabel, _locationField);
            y++;
        }

        var pathLabel = new Label { X = 1, Y = y, Text = "Path:" };
        _pathField = new TextField { X = FieldX, Y = y, Width = Dim.Fill(1), Text = bookmark.Path };
        Add(pathLabel, _pathField);
        y++;

        if (bookmark.Location != null)
        {
            y++;
            var userNameLabel = new Label { X = 1, Y = y, Text = "User name:" };
            _userNameField = new TextField
            {
                X = FieldX, Y = y, Width = Dim.Fill(1), Text = bookmark.UserName ?? string.Empty
            };
            y++;
            var passwordLabel = new Label { X = 1, Y = y, Text = "Password:" };
            _passwordField = new TextField { X = FieldX, Y = y, Width = Dim.Fill(1), Secret = true };
            y++;
            var passwordHintLabel = new Label
            {
                X = FieldX, Y = y, Width = Dim.Fill(1),
                Text = bookmark.HasPassword ? "(empty keeps the stored password)" : "(not stored)"
            };
            y++;
            _storePasswordCheckBox = new CheckBox
            {
                X = FieldX,
                Y = y,
                Text = $"_Store the password in {credentialStoreName}",
                CheckedState = bookmark.HasPassword ? CheckState.Checked : CheckState.UnChecked
            };
            Add(userNameLabel, _userNameField, passwordLabel, _passwordField, passwordHintLabel,
                _storePasswordCheckBox);
            y++;
        }

        y++;
        _errorLabel = new Label { X = 1, Y = y, Width = Dim.Fill(1), Text = string.Empty };
        Add(_errorLabel);

        // the border, the error row and the buttons with their shadow
        Height = y + 5;

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            var changes = new BookmarkChanges(_descriptionField.Text, _locationField?.Text, _pathField.Text,
                _userNameField?.Text, _passwordField?.Text,
                _storePasswordCheckBox?.CheckedState == CheckState.Checked);
            if (changes.GetError(_bookmark) is { } error)
            {
                _errorLabel.Text = error;
                return;
            }

            Result = changes;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _descriptionField.SetFocus();
    }

    /// <summary>Changes entered on OK, otherwise null.</summary>
    public BookmarkChanges? Result { get; private set; }

    private void UpdatePathFromLocation()
    {
        if (_locationField == null || _pathField.Text != _locationPath ||
            _bookmark.GetPathInLocation(_locationField.Text) is not { } path)
        {
            return;
        }

        _locationPath = path;
        _pathField.Text = path;
    }
}
