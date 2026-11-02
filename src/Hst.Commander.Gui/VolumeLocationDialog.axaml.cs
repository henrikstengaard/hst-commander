using Avalonia.Controls;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the location of a volume provider chosen with Alt+F1/Alt+F2 that needs one, e.g. the SMB
///     path "\\server\share", titled by the volume provider with examples below the text box. The dialog returns the
///     location on OK or null on cancel.
/// </summary>
public partial class VolumeLocationDialog : Window
{
    /// <summary>For the XAML previewer.</summary>
    public VolumeLocationDialog()
    {
        InitializeComponent();
    }

    public VolumeLocationDialog(ILocationVolumeProvider volumeProvider, string location) : this()
    {
        Title = volumeProvider.Name;
        LocationLabel.Text = $"Enter {volumeProvider.LocationLabel}:";
        LocationTextBox.Text = location;
        ExampleText.Text = $"e.g. {volumeProvider.LocationExample}";

        OkButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(LocationTextBox.Text))
            {
                Close(LocationTextBox.Text);
            }
        };
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            LocationTextBox.Focus();
            LocationTextBox.SelectAll();
        };
    }
}
