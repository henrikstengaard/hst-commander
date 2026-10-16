using Hst.Commander.Abstractions;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal location dialog of a pane, opened with Ctrl+L or by double-clicking the path. The path field starts with
///     the current path; OK changes to the entered path. Volumes opens the volume provider list, choosing one closes the
///     dialog. <see cref="Result" /> holds the path or volume provider, or null on cancel.
/// </summary>
public sealed class LocationDialog : Dialog
{
    private readonly TextField _pathField;

    /// <param name="path">Current path of the pane.</param>
    /// <param name="chooseVolumeProvider">Shows the volume provider list, returning the chosen one or null.</param>
    /// <param name="palette">Palette of the theme.</param>
    public LocationDialog(string path, Func<IVolumeProvider?> chooseVolumeProvider, TuiPalette palette)
    {
        Title = "Location";
        Width = Dim.Percent(70);
        Height = 8;
        ColorScheme = palette.DialogColorScheme;

        var pathLabel = new Label { X = 1, Y = 1, Text = "Path:" };
        _pathField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = path };

        var volumesButton = new Button { Text = "_Volumes..." };
        volumesButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            if (chooseVolumeProvider() is not { } volumeProvider)
            {
                _pathField.SetFocus();
                return;
            }

            Result = new LocationDialogResult(null, volumeProvider);
            Application.RequestStop(this);
        };
        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = new LocationDialogResult(_pathField.Text, null);
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(pathLabel, _pathField);
        AddButton(volumesButton);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _pathField.SetFocus();
    }

    /// <summary>Path entered on OK or volume provider chosen with Volumes, otherwise null.</summary>
    public LocationDialogResult? Result { get; private set; }
}

/// <summary>Result of the location dialog: either a path to change to or a volume provider to show.</summary>
public sealed record LocationDialogResult(string? Path, IVolumeProvider? VolumeProvider);
