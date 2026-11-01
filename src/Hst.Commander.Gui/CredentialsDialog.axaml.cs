using Avalonia.Controls;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the user name and password of a location that needs them, e.g. an SMB share, with the
///     reason above them. The password isn't shown. The dialog returns the credentials on OK or null on cancel.
/// </summary>
public partial class CredentialsDialog : Window
{
    /// <summary>For the XAML previewer.</summary>
    public CredentialsDialog()
    {
        InitializeComponent();
    }

    public CredentialsDialog(CredentialsRequest request) : this()
    {
        MessageText.Text = request.Message;
        UserNameTextBox.Text = request.UserName;

        OkButton.Click += (_, _) => Close(new VolumeCredentials((UserNameTextBox.Text ?? string.Empty).Trim(),
            PasswordTextBox.Text ?? string.Empty));
        CancelButton.Click += (_, _) => Close();

        // the password is entered next when the user name is known
        Opened += (_, _) => (request.UserName.Length > 0 ? PasswordTextBox : UserNameTextBox).Focus();
    }
}
