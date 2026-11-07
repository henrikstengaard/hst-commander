using Avalonia.Controls;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the connection details and credentials of a volume provider chosen with Alt+F1/Alt+F2
///     that needs them (<see cref="IConnectionVolumeProvider" />, e.g. FTP/SFTP): the protocol, host, port (empty for the
///     protocol's default port, shown as placeholder text), path (empty for the home directory), user name and password.
///     Shows the last connection details and user name entered. Invalid input is shown below the fields and keeps the
///     dialog open. The dialog returns the <see cref="ConnectionRequest" /> on OK or null on cancel.
/// </summary>
public partial class ConnectionDialog : Window
{
    /// <summary>For the XAML previewer.</summary>
    public ConnectionDialog()
    {
        InitializeComponent();
    }

    public ConnectionDialog(IConnectionVolumeProvider volumeProvider) : this()
    {
        Title = volumeProvider.Name;
        var details = VolumeConnector.GetLastConnectionDetails(volumeProvider);
        var protocols = volumeProvider.Protocols;

        ProtocolComboBox.ItemsSource = protocols.Select(protocol => protocol.Label).ToList();
        ProtocolComboBox.SelectedIndex =
            Math.Max(0, protocols.ToList().FindIndex(protocol => protocol.Name == details.Protocol));
        HostTextBox.Text = details.Host;
        PortTextBox.Text = details.Port?.ToString() ?? string.Empty;
        PathTextBox.Text = details.Path;
        UserNameTextBox.Text = VolumeConnector.GetLastUserName(volumeProvider);

        string SelectedProtocol() => protocols.Count == 0
            ? string.Empty
            : protocols[Math.Clamp(ProtocolComboBox.SelectedIndex, 0, protocols.Count - 1)].Name;

        void UpdateDefaultPort() =>
            PortTextBox.PlaceholderText = VolumeConnector.GetDefaultPortText(volumeProvider, SelectedProtocol());

        ProtocolComboBox.SelectionChanged += (_, _) => UpdateDefaultPort();
        UpdateDefaultPort();

        OkButton.Click += (_, _) =>
        {
            try
            {
                Close(VolumeConnector.CreateConnectionRequest(volumeProvider, SelectedProtocol(),
                    HostTextBox.Text ?? string.Empty, PortTextBox.Text ?? string.Empty,
                    PathTextBox.Text ?? string.Empty, UserNameTextBox.Text ?? string.Empty,
                    PasswordTextBox.Text ?? string.Empty));
            }
            catch (IOException exception)
            {
                ErrorText.Text = exception.Message;
                ErrorText.IsVisible = true;
            }
        };
        CancelButton.Click += (_, _) => Close();

        // the password is entered next when the host and user name are known
        Opened += (_, _) =>
            (details.Host.Length == 0 ? HostTextBox
                : string.IsNullOrEmpty(UserNameTextBox.Text) ? UserNameTextBox
                : PasswordTextBox).Focus();
    }
}
