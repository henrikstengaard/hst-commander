using Avalonia.Controls;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal location dialog of a pane, opened with Ctrl+L or by double-clicking the path. The path text box starts
///     with the current path; OK returns the entered path. Volumes opens the volume provider list over this dialog,
///     choosing one closes the dialog with it. The dialog returns null when cancelled.
/// </summary>
public partial class LocationDialog : Window
{
    public LocationDialog() : this(string.Empty, _ => Task.FromResult<IVolumeProvider?>(null))
    {
    }

    /// <param name="path">Current path of the pane.</param>
    /// <param name="chooseVolumeProvider">
    ///     Shows the volume provider list owned by the given window, returning the chosen one or null.
    /// </param>
    public LocationDialog(string path, Func<Window, Task<IVolumeProvider?>> chooseVolumeProvider)
    {
        InitializeComponent();
        PathTextBox.Text = path;
        VolumesButton.Click += async (_, _) =>
        {
            if (await chooseVolumeProvider(this) is { } volumeProvider)
            {
                Close(new LocationDialogResult(null, volumeProvider));
                return;
            }

            PathTextBox.Focus();
        };
        OkButton.Click += (_, _) => Close(new LocationDialogResult(PathTextBox.Text ?? string.Empty, null));
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            PathTextBox.Focus();
            PathTextBox.SelectAll();
        };
    }
}

/// <summary>Result of the location dialog: either a path to change to or a volume provider to show.</summary>
public sealed record LocationDialogResult(string? Path, IVolumeProvider? VolumeProvider);
