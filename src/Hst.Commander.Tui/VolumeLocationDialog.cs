using Hst.Commander.Abstractions;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the location of a volume provider chosen with Alt+F1/Alt+F2 that needs one, e.g. the SMB
///     path "\\server\share", titled by the volume provider with examples below the text field.
///     <see cref="Result" /> holds the location on OK or null on cancel.
/// </summary>
public sealed class VolumeLocationDialog : Dialog
{
    private readonly TextField _locationField;

    public VolumeLocationDialog(ILocationVolumeProvider volumeProvider, string location, TuiPalette palette)
    {
        Title = volumeProvider.Name;
        Width = Dim.Percent(60);
        Height = 9;
        ColorScheme = palette.DialogColorScheme;

        var locationLabel = new Label { X = 1, Y = 1, Text = $"Enter {volumeProvider.LocationLabel}:" };
        _locationField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = location };
        var exampleLabel = new Label { X = 1, Y = 3, Text = $"e.g. {volumeProvider.LocationExample}" };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            if (string.IsNullOrWhiteSpace(_locationField.Text))
            {
                return;
            }

            Result = _locationField.Text;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(locationLabel, _locationField, exampleLabel);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _locationField.SetFocus();
    }

    /// <summary>Location entered when OK was pressed, otherwise null.</summary>
    public string? Result { get; private set; }
}
