using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the user name and password of a location that needs them, e.g. an SMB share, with the
///     reason above them. The password isn't shown. <see cref="Result" /> holds the credentials on OK or null on cancel.
/// </summary>
public sealed class CredentialsDialog : Dialog
{
    private readonly TextField _userNameField;
    private readonly TextField _passwordField;

    public CredentialsDialog(CredentialsRequest request, TuiPalette palette)
    {
        Title = "Enter credentials";
        Width = Dim.Percent(60);
        Height = 14;
        ColorScheme = palette.DialogColorScheme;

        // up to 3 lines, e.g. why a bookmark's stored password wasn't used
        var messageLabel = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Height = 3, Text = request.Message };
        messageLabel.TextFormatter.WordWrap = true;
        var userNameLabel = new Label { X = 1, Y = 4, Text = "User name:" };
        _userNameField = new TextField { X = 1, Y = 5, Width = Dim.Fill(1), Text = request.UserName };
        var passwordLabel = new Label { X = 1, Y = 7, Text = "Password:" };
        _passwordField = new TextField { X = 1, Y = 8, Width = Dim.Fill(1), Secret = true };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = new VolumeCredentials(_userNameField.Text.Trim(), _passwordField.Text);
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(messageLabel, userNameLabel, _userNameField, passwordLabel, _passwordField);
        AddButton(okButton);
        AddButton(cancelButton);

        // the password is entered next when the user name is known
        Initialized += (_, _) =>
            (request.UserName.Length > 0 ? _passwordField : _userNameField).SetFocus();
    }

    /// <summary>User name and password entered when OK was pressed, otherwise null.</summary>
    public VolumeCredentials? Result { get; private set; }
}
