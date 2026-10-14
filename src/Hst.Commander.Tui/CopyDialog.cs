using Hst.Commander.Core.Volumes;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal confirm dialog shown before copying or moving, where the destination and the filter of the files to copy
///     or move can be changed. <see cref="Result" /> holds the destination and filter on OK or null on cancel.
/// </summary>
public sealed class CopyDialog : Dialog
{
    private readonly TextField _destinationField;
    private readonly TextField _filterField;

    public CopyDialog(string description, string destination, TuiPalette palette, string title = "Copy")
    {
        Title = title;
        Width = Dim.Percent(70);
        Height = 11;
        ColorScheme = palette.DialogColorScheme;

        var destinationLabel = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = $"{description} to:" };
        _destinationField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = destination };
        var filterLabel = new Label { X = 1, Y = 4, Text = "Filter:" };
        _filterField = new TextField { X = 1, Y = 5, Width = Dim.Fill(1), Text = EntryFilter.All };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = new CopyDialogResult(_destinationField.Text, _filterField.Text);
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(destinationLabel, _destinationField, filterLabel, _filterField);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _destinationField.SetFocus();
    }

    /// <summary>Destination and filter when OK was pressed, otherwise null.</summary>
    public CopyDialogResult? Result { get; private set; }
}

public sealed record CopyDialogResult(string Destination, string Filter);
