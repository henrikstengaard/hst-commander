using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the connection details and credentials of a volume provider chosen with Alt+F1/Alt+F2
///     that needs them (<see cref="IConnectionVolumeProvider" />, e.g. FTP/SFTP): the protocol, host, port (empty for the
///     protocol's default port), path (empty for the home directory), user name and password. Shows the last connection
///     details and user name entered. Invalid input is shown below the fields and keeps the dialog open.
///     <see cref="Result" /> holds the location and credentials on OK or null on cancel.
/// </summary>
public sealed class ConnectionDialog : Dialog
{
    private readonly IConnectionVolumeProvider _volumeProvider;
    private readonly RadioGroup _protocolRadioGroup;
    private readonly TextField _hostField;
    private readonly TextField _portField;
    private readonly Label _defaultPortLabel;
    private readonly TextField _pathField;
    private readonly TextField _userNameField;
    private readonly TextField _passwordField;
    private readonly Label _errorLabel;

    public ConnectionDialog(IConnectionVolumeProvider volumeProvider, TuiPalette palette)
    {
        _volumeProvider = volumeProvider;
        Title = volumeProvider.Name;
        Width = Dim.Percent(60);
        Height = 17 + volumeProvider.Protocols.Count;
        ColorScheme = palette.DialogColorScheme;

        var details = VolumeConnector.GetLastConnectionDetails(volumeProvider);
        var protocols = volumeProvider.Protocols;

        var protocolLabel = new Label { X = 1, Y = 1, Text = "Protocol:" };
        _protocolRadioGroup = new RadioGroup
        {
            X = 12,
            Y = 1,
            RadioLabels = protocols.Select(protocol => protocol.Label).ToArray(),
            SelectedItem = Math.Max(0, protocols.ToList().FindIndex(protocol => protocol.Name == details.Protocol))
        };
        var y = 1 + protocols.Count + 1;

        var hostLabel = new Label { X = 1, Y = y, Text = "Host:" };
        _hostField = new TextField { X = 12, Y = y, Width = Dim.Fill(1), Text = details.Host };
        y++;
        var portLabel = new Label { X = 1, Y = y, Text = "Port:" };
        _portField = new TextField { X = 12, Y = y, Width = 7, Text = details.Port?.ToString() ?? string.Empty };
        _defaultPortLabel = new Label { X = 20, Y = y };
        y++;
        var pathLabel = new Label { X = 1, Y = y, Text = "Path:" };
        _pathField = new TextField { X = 12, Y = y, Width = Dim.Fill(1), Text = details.Path };
        y++;
        var pathHintLabel = new Label { X = 12, Y = y, Text = "(empty for the home directory)" };
        y += 2;
        var userNameLabel = new Label { X = 1, Y = y, Text = "User name:" };
        _userNameField = new TextField
        {
            X = 12, Y = y, Width = Dim.Fill(1), Text = VolumeConnector.GetLastUserName(volumeProvider)
        };
        y++;
        var passwordLabel = new Label { X = 1, Y = y, Text = "Password:" };
        _passwordField = new TextField { X = 12, Y = y, Width = Dim.Fill(1), Secret = true };
        y += 2;
        _errorLabel = new Label { X = 1, Y = y, Width = Dim.Fill(1), Text = string.Empty };

        _protocolRadioGroup.SelectedItemChanged += (_, _) => UpdateDefaultPort();
        UpdateDefaultPort();

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            try
            {
                Result = VolumeConnector.CreateConnectionRequest(_volumeProvider, SelectedProtocol,
                    _hostField.Text, _portField.Text, _pathField.Text, _userNameField.Text, _passwordField.Text);
            }
            catch (IOException exception)
            {
                _errorLabel.Text = exception.Message;
                return;
            }

            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(protocolLabel, _protocolRadioGroup, hostLabel, _hostField, portLabel, _portField, _defaultPortLabel,
            pathLabel, _pathField, pathHintLabel, userNameLabel, _userNameField, passwordLabel, _passwordField,
            _errorLabel);
        AddButton(okButton);
        AddButton(cancelButton);

        // the password is entered next when the host and user name are known
        Initialized += (_, _) =>
            (details.Host.Length == 0 ? _hostField
                : _userNameField.Text.Length == 0 ? _userNameField
                : _passwordField).SetFocus();
    }

    /// <summary>Location and credentials entered when OK was pressed, otherwise null.</summary>
    public ConnectionRequest? Result { get; private set; }

    private string SelectedProtocol =>
        _volumeProvider.Protocols.Count == 0
            ? string.Empty
            : _volumeProvider.Protocols[Math.Clamp(_protocolRadioGroup.SelectedItem, 0,
                _volumeProvider.Protocols.Count - 1)].Name;

    private void UpdateDefaultPort() =>
        _defaultPortLabel.Text = $"(default {VolumeConnector.GetDefaultPortText(_volumeProvider, SelectedProtocol)})";
}
