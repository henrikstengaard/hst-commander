using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal location dialog of a pane, opened with Ctrl+L or by double-clicking the path. The path text box starts
///     with the current path; OK returns the entered path. Volumes opens the volume provider list over this dialog,
///     choosing one closes the dialog with it. Home goes to the user's home directory. The dialog returns null when cancelled.
/// </summary>
public partial class LocationDialog : Window
{
    public LocationDialog() : this(string.Empty, _ => Task.FromResult<VolumeChoice?>(null))
    {
    }

    /// <param name="path">Current path of the pane.</param>
    /// <param name="chooseVolumeProvider">
    ///     Shows the volume provider list owned by the given window, returning the chosen volume provider or bookmark, or null.
    /// </param>
    public LocationDialog(string path, Func<Window, Task<VolumeChoice?>> chooseVolumeProvider)
    {
        InitializeComponent();
        PathTextBox.Text = path;
        VolumesButton.Click += async (_, _) =>
        {
            if (await chooseVolumeProvider(this) is { } volumeChoice)
            {
                Close(new LocationDialogResult(null, volumeChoice));
                return;
            }

            PathTextBox.Focus();
        };
        HomeButton.Click += (_, _) => Close(new LocationDialogResult(null, null, true));
        OkButton.Click += (_, _) => Close(new LocationDialogResult(PathTextBox.Text ?? string.Empty, null));
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            PathTextBox.Focus();
            PathTextBox.SelectAll();
        };
    }
}

/// <summary>
///     Result of the location dialog: a path to change to, a volume provider or bookmark to show or going to the home
///     directory.
/// </summary>
public sealed record LocationDialogResult(string? Path, VolumeChoice? VolumeChoice, bool IsHome = false);
